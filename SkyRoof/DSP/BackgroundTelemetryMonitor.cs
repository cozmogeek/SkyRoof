using MathNet.Numerics;
using Serilog;
using SkyRoof.Satellites;
using VE3NEA;
using VE3NEA.SkyTlm.Core;

namespace SkyRoof
{
  /// <summary>
  /// Extra Doppler-tracked 48 kHz channels for in-band satellites that are not the UI selection.
  /// The selected transmitter stays on the main <see cref="Slicer"/>; this only demodulates the others
  /// already sitting in the current SDR passband.
  /// </summary>
  internal sealed class BackgroundTelemetryMonitor : IDisposable
  {
    // each channel is a full mix/decimate thread on the SDR rate; a handful is plenty for one passband
    internal const int MaxChannels = 8;

    private readonly Context ctx;
    private readonly Action<TelemetryDecocder, SatnogsDbSatellite, SatnogsDbTransmitter, SignalParams, int> bind;
    private readonly Func<(string? SatId, string? TransmitterUuid)> foreground;
    private readonly object gate = new();
    private readonly Dictionary<string, Channel> channels = new();
    private bool disposed;

    public BackgroundTelemetryMonitor(
      Context ctx,
      Func<(string? SatId, string? TransmitterUuid)> foreground,
      Action<TelemetryDecocder, SatnogsDbSatellite, SatnogsDbTransmitter, SignalParams, int> bind)
    {
      this.ctx = ctx;
      this.foreground = foreground;
      this.bind = bind;
    }

    public void ProcessWidebandIq(DataEventArgs<Complex32> e)
    {
      Channel[] snapshot;
      lock (gate)
      {
        if (disposed || channels.Count == 0) return;
        snapshot = channels.Values.ToArray();
      }

      foreach (var channel in snapshot)
        channel.Slicer.StartProcessing(e);
    }

    /// <summary>Refresh mix offsets from the latest SGP4 observation (a few times per second).</summary>
    public void TickDoppler()
    {
      Channel[] snapshot;
      lock (gate)
      {
        if (disposed || channels.Count == 0) return;
        snapshot = channels.Values.ToArray();
      }

      var engine = PassEngine();
      double sdrHz = ctx.Sdr?.Frequency ?? 0;
      bool xverter = ctx.Settings.Transverter.SdrOffsetEnabled;

      foreach (var channel in snapshot)
      {
        if (!TryCorrectedRf(channel, engine, out double rf, out double rate)) continue;
        if (!TrySlicerOffset(rf, sdrHz, xverter, out double offset)) continue;
        channel.Slicer.SetOffset(offset, rate);
      }
    }

    /// <summary>
    /// Create/drop channels so the set matches sats currently above the horizon whose downlinks
    /// fall in the SDR passband, excluding the transmitter the main decoder already owns.
    /// </summary>
    public void Sync()
    {
      if (disposed) return;

      var wanted = ListWanted();
      var wantedIds = new HashSet<string>(wanted.Select(w => w.Tx.uuid));

      List<Channel> removed = new();
      List<Target> toStart = new();
      lock (gate)
      {
        if (disposed) return;

        foreach (var id in channels.Keys.ToArray())
          if (!wantedIds.Contains(id))
          {
            removed.Add(channels[id]);
            channels.Remove(id);
          }

        foreach (var target in wanted)
          if (!channels.ContainsKey(target.Tx.uuid)) toStart.Add(target);
      }

      foreach (var channel in removed)
      {
        Log.Information("Background telemetry: stop {Sat} {Tx}", channel.Satellite.name, channel.Transmitter.description);
        channel.Dispose();
      }

      foreach (var target in toStart)
      {
        var channel = CreateChannel(target);
        if (channel == null) continue;
        lock (gate)
        {
          if (disposed || !wantedIds.Contains(target.Tx.uuid) || channels.ContainsKey(target.Tx.uuid))
          {
            channel.Dispose();
            continue;
          }
          channels.Add(target.Tx.uuid, channel);
        }
      }
    }

    public void Clear()
    {
      List<Channel> removed;
      lock (gate)
      {
        removed = channels.Values.ToList();
        channels.Clear();
      }
      foreach (var channel in removed) channel.Dispose();
    }

    public void Dispose()
    {
      disposed = true;
      Clear();
    }

    private Channel? CreateChannel(Target target)
    {
      double rate = ctx.Sdr!.Info.SampleRate;
      if (rate <= 0) return null;

      var slicer = new Slicer(rate, 0, Slicer.Mode.FM_D);
      slicer.Enabled = true;
      var decoder = new TelemetryDecocder(target.Params, target.Sat.norad_cat_id, telemetry: true, sstv: false,
        fmEngine: null);

      var channel = new Channel(target.Sat, target.Tx, target.Params, target.NominalHz, target.Orbit, slicer, decoder);
      slicer.IqDataAvailable += (_, iq) => decoder.StartProcessing(iq);
      bind(decoder, target.Sat, target.Tx, target.Params, target.Orbit);

      var engine = PassEngine();
      double sdrHz = ctx.Sdr.Frequency;
      bool xverter = ctx.Settings.Transverter.SdrOffsetEnabled;
      if (TryCorrectedRf(channel, engine, out double rf, out double dopplerRate)
        && TrySlicerOffset(rf, sdrHz, xverter, out double offset))
        slicer.SetOffset(offset, dopplerRate);

      Log.Information("Background telemetry: start {Sat} {Tx} @ {Hz:F0} Hz",
        target.Sat.name, target.Tx.description, target.NominalHz);
      return channel;
    }

    private List<Target> ListWanted()
    {
      var result = new List<Target>();
      if (!ctx.Settings.Telemetry.BackgroundDecode) return result;
      if (ctx.Sdr?.Info == null) return result;

      var skip = foreground();
      var engine = PassEngine();
      double sdrHz = ctx.Sdr.Frequency;
      bool xverter = ctx.Settings.Transverter.SdrOffsetEnabled;
      var now = DateTime.UtcNow;

      foreach (var pass in engine.GetPassesSnapshot())
      {
        if (!pass.IsAboveHorizon()) continue;
        // the selected sat is already on the main slicer; a second pipeline on the same bird
        // double-files every frame under that pass node
        if (skip.SatId != null && pass.Satellite.sat_id == skip.SatId) continue;
        if (skip.SatId == null && skip.TransmitterUuid != null
          && pass.Satellite.Transmitters.Any(t => t.uuid == skip.TransmitterUuid)) continue;

        double elevation = pass.GetObservationAt(now)?.Elevation.Degrees ?? 0;

        foreach (var group in pass.Satellite.Transmitters
          .Where(tx => tx.downlink_low != null)
          .GroupBy(tx => tx.DownlinkLow))
        {
          var tx = PickTelemetryTransmitter(pass.Satellite, group);
          if (tx == null) continue;
          if (tx.uuid == skip.TransmitterUuid) continue;

          var parameters = SignalParamsResolver.Resolve(tx);
          if (!SignalParamsResolver.IsTelemetryDecodable(parameters)) continue;
          if (AutoRecorder.NeedsWidebandIq(tx)) continue;
          if (IsLinearTransponder(tx)) continue;

          double nominal = tx.DownlinkLow;
          var observation = engine.ObserveSatellite(pass.Satellite, now);
          double rf = observation == null ? nominal : nominal * (1 - observation.RangeRate / 3e5);
          if (!TrySlicerOffset(rf, sdrHz, xverter, out _)) continue;

          result.Add(new Target(pass.Satellite, tx, parameters!, nominal, pass.OrbitNumber, elevation));
        }
      }

      return result
        .OrderByDescending(t => t.Elevation)
        .ThenByDescending(t => t.Tx.alive)
        .ThenByDescending(t => t.Params.Baud)
        .Take(MaxChannels)
        .ToList();
    }

    private SatellitePasses PassEngine()
    {
      bool ham = ctx.Sdr == null || SatnogsDbTransmitter.IsHamFrequency(ctx.FrequencyControl.GetSdrRfCenter());
      return ham ? ctx.HamPasses : ctx.SdrPasses;
    }

    internal static SatnogsDbTransmitter? PickTelemetryTransmitter(
      SatnogsDbSatellite satellite, IEnumerable<SatnogsDbTransmitter> sameFrequency)
    {
      return sameFrequency
        .Select(tx => (Tx: tx, Params: SignalParamsResolver.Resolve(tx)))
        .Where(c => SignalParamsResolver.IsTelemetryDecodable(c.Params)
          && !AutoRecorder.NeedsWidebandIq(c.Tx)
          && !IsLinearTransponder(c.Tx))
        .OrderByDescending(c => c.Tx.alive)
        .ThenByDescending(c => c.Params!.Baud)
        .Select(c => c.Tx)
        .FirstOrDefault();
    }

    internal static bool IsLinearTransponder(SatnogsDbTransmitter tx)
    {
      if (tx.type == "Transponder") return true;
      return tx.downlink_high.HasValue && tx.downlink_high != tx.downlink_low
        && tx.uplink_low.HasValue && tx.uplink_high.HasValue;
    }

    private bool TryCorrectedRf(Channel channel, SatellitePasses engine, out double rfHz, out double rateHzPerSec)
    {
      var now = DateTime.UtcNow;
      var observation = engine.ObserveSatellite(channel.Satellite, now);
      if (observation == null)
      {
        rfHz = channel.NominalHz;
        rateHzPerSec = 0;
        channel.Doppler.Update(0, false, channel.Satellite, now);
        return false;
      }

      double factor = observation.RangeRate / 3e5;
      channel.Doppler.Update(factor, true, channel.Satellite, now);
      rfHz = channel.NominalHz * (1 - factor);
      rateHzPerSec = -channel.NominalHz * channel.Doppler.FactorRate;
      return true;
    }

    private bool TrySlicerOffset(double rfHz, double sdrHz, bool xverter, out double offsetHz)
    {
      offsetHz = 0;
      if (ctx.Sdr?.Info == null) return false;

      double ifHz = rfHz;
      if (xverter)
      {
        var band = ctx.Settings.Transverter.GetSdrBand(rfHz);
        if (band != null) ifHz = rfHz - band.LoOffset;
      }

      double wing = ctx.Sdr.Info.MaxBandwidth / 2;
      if (ifHz < sdrHz - wing || ifHz > sdrHz + wing) return false;
      offsetHz = ifHz - sdrHz;
      return true;
    }

    private readonly record struct Target(
      SatnogsDbSatellite Sat,
      SatnogsDbTransmitter Tx,
      SignalParams Params,
      double NominalHz,
      int Orbit,
      double Elevation);

    private sealed class Channel : IDisposable
    {
      public readonly SatnogsDbSatellite Satellite;
      public readonly SatnogsDbTransmitter Transmitter;
      public readonly SignalParams Params;
      public readonly double NominalHz;
      public readonly int Orbit;
      public readonly Slicer Slicer;
      public readonly TelemetryDecocder Decoder;
      public readonly DopplerRateEstimator Doppler = new();

      public Channel(SatnogsDbSatellite satellite, SatnogsDbTransmitter transmitter, SignalParams signalParams,
        double nominalHz, int orbit, Slicer slicer, TelemetryDecocder decoder)
      {
        Satellite = satellite;
        Transmitter = transmitter;
        Params = signalParams;
        NominalHz = nominalHz;
        Orbit = orbit;
        Slicer = slicer;
        Decoder = decoder;
      }

      public void Dispose()
      {
        Slicer.Enabled = false;
        Slicer.Dispose();
        Decoder.Purge();
        Decoder.Dispose();
      }
    }
  }
}
