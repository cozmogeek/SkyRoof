using NAudio.Wave;
using MathNet.Numerics;
using VE3NEA;
using System.Buffers;
using System.Threading.Channels;

namespace SkyRoof
{
  public class AutoRecorder
  {
    private readonly Context ctx;
    private readonly object gate = new();

    private Stream? output;
    private bool isAudio;
    private bool isWideband;
    private int iqSampleRate;
    private string? satId;
    private string? fileName;

    private Channel<WriteChunk>? channel;
    private CancellationTokenSource? writerCts;
    private Task? writerTask;

    /// <summary>
    /// extra headroom after AF gain so peaks rarely hit 0 dBFS in the WAV (speaker path can still clip in hardware).
    /// </summary>
    private const float PcmHeadroom = 0.92f;

    private sealed class WriteChunk
    {
      public byte[] Buffer = Array.Empty<byte>();
      public int Count;
    }

    public bool IsRecording
    {
      get { lock (gate) return output != null; }
    }

    public AutoRecorder(Context ctx)
    {
      this.ctx = ctx;
    }

    public void EnsureRecording(string satId, string satName, int? maxElevationDeg, AutoRecordMode mode,
      int iqSampleRate = SdrConst.AUDIO_SAMPLING_RATE, bool wideband = false)
    {
      lock (gate)
      {
        if (mode == AutoRecordMode.Off)
        {
          Stop_NoLock();
          return;
        }

        bool wantAudio = mode == AutoRecordMode.Audio;
        if (wantAudio) wideband = false;

        if (output != null && this.satId == satId && isAudio == wantAudio
          && this.isWideband == wideband && this.iqSampleRate == iqSampleRate)
          return;

        Stop_NoLock();

        string recordingsDir = Path.Combine(Utils.GetUserDataFolder(), "Recordings");
        Directory.CreateDirectory(recordingsDir);

        string utc = DateTime.UtcNow.ToString("yyyy-MM-dd_HH_mm_ss", System.Globalization.CultureInfo.InvariantCulture);
        string safeSat = Utils.SanitizeFileNamePart(satName);
        string el = maxElevationDeg == null ? "" : $"_{Math.Clamp(maxElevationDeg.Value, 0, 90):00}deg";

        // Wideband IQ is RF64: classic WAV is capped at 4 GB (~53 s at 10 Msps float IQ).
        // SDR# baseband player opens wav/raw/rf64/dd; RF64 keeps sample-rate in the header.
        // Audio and 48 kHz slicer IQ stay WAV (small enough for the RIFF limit).
        bool rf64Iq = !wantAudio && wideband;
        string suffix = wantAudio ? "" : $"_IQ_{iqSampleRate}SPS";
        string ext = rf64Iq ? ".rf64" : ".wav";
        fileName = Path.Combine(recordingsDir, $"{utc}Z_{safeSat}{el}{suffix}{ext}");

        if (wantAudio)
          output = new WaveFileWriter(fileName, new WaveFormat(SdrConst.AUDIO_SAMPLING_RATE, 16, 1));
        else if (rf64Iq)
          output = new Rf64WaveWriter(fileName, iqSampleRate, channels: 2, bitsPerSample: 32, ieeeFloat: true);
        else
          output = new WaveFileWriter(fileName, WaveFormat.CreateIeeeFloatWaveFormat(iqSampleRate, 2));

        this.satId = satId;
        isAudio = wantAudio;
        isWideband = wideband;
        this.iqSampleRate = iqSampleRate;

        int queueDepth = wideband ? 256 : 64;
        channel = Channel.CreateBounded<WriteChunk>(new BoundedChannelOptions(queueDepth)
        {
          FullMode = BoundedChannelFullMode.DropOldest,
          SingleReader = true,
          SingleWriter = false,
        });
        writerCts = new CancellationTokenSource();
        writerTask = Task.Run(() => WriterLoop(writerCts.Token));
      }
    }

    public void Stop()
    {
      lock (gate) Stop_NoLock();
    }

    private void Stop_NoLock()
    {
      try
      {
        if (writerCts != null && !writerCts.IsCancellationRequested)
          writerCts.Cancel();
      }
      catch { }

      try
      {
        channel?.Writer.TryComplete();
      }
      catch { }

      var task = writerTask;
      writerTask = null;

      // let the background writer drain the channel before disposing the file.
      try
      {
        task?.Wait(TimeSpan.FromSeconds(2));
      }
      catch { }

      output?.Dispose();
      output = null;
      satId = null;
      fileName = null;
      isWideband = false;
      iqSampleRate = 0;
      writerCts?.Dispose();
      writerCts = null;
      channel = null;
    }

    public void AddAudioSamples(float[] data, int count)
    {
      ChannelWriter<WriteChunk>? w;
      lock (gate)
      {
        if (output == null || !isAudio) return;
        if (count <= 0) return;
        w = channel?.Writer;
      }
      if (w == null) return;

      int bytes = count * sizeof(short);
      byte[] buffer = ArrayPool<byte>.Shared.Rent(bytes);

      // match what you hear: <see cref="GainWidget"/> applies AF gain inside SpeakerSoundcard; we tap pre-gain floats.
      float af = Dsp.FromDb2(ctx.Settings.Audio.SoundcardVolume);

      // float [-1..1] -> PCM16 (write directly into byte buffer)
      for (int i = 0; i < count; i++)
      {
        float v = Math.Clamp(data[i] * af, -1f, 1f) * PcmHeadroom;
        short s = (short)Math.Clamp(v * short.MaxValue, short.MinValue, short.MaxValue);
        buffer[2 * i] = (byte)(s & 0xFF);
        buffer[2 * i + 1] = (byte)((s >> 8) & 0xFF);
      }

      var chunk = new WriteChunk { Buffer = buffer, Count = bytes };
      if (!w.TryWrite(chunk))
        ArrayPool<byte>.Shared.Return(buffer);
    }

    public void AddIqSamples(Complex32[] data, int count, bool wideband)
    {
      ChannelWriter<WriteChunk>? w;
      lock (gate)
      {
        if (output == null || isAudio || isWideband != wideband) return;
        if (count <= 0) return;
        w = channel?.Writer;
      }
      if (w == null) return;

      // stereo IEEE float32 => 8 bytes per IQ sample (I,Q) — SatDump cf32
      int bytes = count * 2 * sizeof(float);
      byte[] buffer = ArrayPool<byte>.Shared.Rent(bytes);

      for (int i = 0; i < count; i++)
      {
        int o = i * 8;
        WriteFloatLe(buffer, o, data[i].Real);
        WriteFloatLe(buffer, o + 4, data[i].Imaginary);
      }

      var chunk = new WriteChunk { Buffer = buffer, Count = bytes };
      if (!w.TryWrite(chunk))
        ArrayPool<byte>.Shared.Return(buffer);
    }

    private static void WriteFloatLe(byte[] buffer, int offset, float value)
    {
      BitConverter.TryWriteBytes(buffer.AsSpan(offset, 4), value);
    }

    private async Task WriterLoop(CancellationToken ct)
    {
      ChannelReader<WriteChunk>? r;
      Stream? localOutput;
      lock (gate)
      {
        r = channel?.Reader;
        localOutput = output;
      }
      if (r == null || localOutput == null) return;

      try
      {
        while (await r.WaitToReadAsync(ct).ConfigureAwait(false))
        {
          while (r.TryRead(out var chunk))
          {
            try
            {
              localOutput.Write(chunk.Buffer, 0, chunk.Count);
            }
            finally
            {
              ArrayPool<byte>.Shared.Return(chunk.Buffer);
            }
          }
        }
      }
      catch (OperationCanceledException)
      {
        // stop requested
      }
      catch
      {
        // swallow: recording is best-effort; we don't want to impact audio pipeline
      }
    }

    /// <summary>
    /// LRPT/HRPT and other high-rate imaging need more than the 48 kHz slicer IQ
    /// (SatDump SPS must stay above 1). Those recordings tap the SDR stream instead.
    /// </summary>
    public static bool NeedsWidebandIq(SatnogsDbTransmitter? tx)
    {
      if (tx == null) return false;

      // SatNOGS: AHRPT=17, HRPT=45, LRPT=53
      if (tx.mode_id is 17 or 45 or 53) return true;
      if (MentionsWidebandImageMode(tx.DownlinkMode) || MentionsWidebandImageMode(tx.mode)
        || MentionsWidebandImageMode(tx.description))
        return true;

      double baud = tx.baud ?? 0;
      if (tx.gr_sats?.baudrate is double yamlBaud) baud = Math.Max(baud, yamlBaud);
      if (tx.manual?.baudrate is double manualBaud) baud = Math.Max(baud, manualBaud);
      return baud >= SdrConst.AUDIO_SAMPLING_RATE;
    }

    private static bool MentionsWidebandImageMode(string? text)
    {
      if (string.IsNullOrEmpty(text)) return false;
      return text.Contains("LRPT", StringComparison.OrdinalIgnoreCase)
        || text.Contains("HRPT", StringComparison.OrdinalIgnoreCase);
    }
  }
}
