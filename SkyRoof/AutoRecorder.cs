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

    private WaveFileWriter? writer;
    private bool isAudio;
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
      get { lock (gate) return writer != null; }
    }

    public AutoRecorder(Context ctx)
    {
      this.ctx = ctx;
    }

    public void EnsureRecording(string satId, string satName, int? maxElevationDeg, AutoRecordMode mode)
    {
      lock (gate)
      {
        if (mode == AutoRecordMode.Off)
        {
          Stop_NoLock();
          return;
        }

        bool wantAudio = mode == AutoRecordMode.Audio;

        if (writer != null && this.satId == satId && isAudio == wantAudio) return;

        Stop_NoLock();

        string recordingsDir = Path.Combine(Utils.GetUserDataFolder(), "Recordings");
        Directory.CreateDirectory(recordingsDir);

        string utc = DateTime.UtcNow.ToString("yyyy-MM-dd_HH_mm_ss", System.Globalization.CultureInfo.InvariantCulture);
        string safeSat = Utils.SanitizeFileNamePart(satName);
        string el = maxElevationDeg == null ? "" : $"_{Math.Clamp(maxElevationDeg.Value, 0, 90):00}deg";
        string suffix = wantAudio ? "" : "_IQ";
        fileName = Path.Combine(recordingsDir, $"{utc}Z_{safeSat}{el}{suffix}.wav");

        // audio: PCM16 for player compatibility; IQ: IEEE float32 stereo (cf32) for SatDump/tools.
        WaveFormat format = wantAudio
          ? new WaveFormat(SdrConst.AUDIO_SAMPLING_RATE, 16, 1)
          : WaveFormat.CreateIeeeFloatWaveFormat(SdrConst.AUDIO_SAMPLING_RATE, 2);
        writer = new WaveFileWriter(fileName, format);
        this.satId = satId;
        isAudio = wantAudio;

        // start background writer
        channel = Channel.CreateBounded<WriteChunk>(new BoundedChannelOptions(64)
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

      writer?.Dispose();
      writer = null;
      satId = null;
      fileName = null;
      writerCts?.Dispose();
      writerCts = null;
      channel = null;
    }

    public void AddAudioSamples(float[] data, int count)
    {
      ChannelWriter<WriteChunk>? w;
      lock (gate)
      {
        if (writer == null || !isAudio) return;
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

    public void AddIqSamples(Complex32[] data, int count)
    {
      ChannelWriter<WriteChunk>? w;
      lock (gate)
      {
        if (writer == null || isAudio) return;
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
      WaveFileWriter? localWriter;
      lock (gate)
      {
        r = channel?.Reader;
        localWriter = writer;
      }
      if (r == null || localWriter == null) return;

      try
      {
        while (await r.WaitToReadAsync(ct).ConfigureAwait(false))
        {
          while (r.TryRead(out var chunk))
          {
            try
            {
              localWriter.Write(chunk.Buffer, 0, chunk.Count);
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
  }
}

