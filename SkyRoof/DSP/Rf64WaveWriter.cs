using System.Text;

namespace SkyRoof
{
  /// <summary>
  /// RF64 (EBU Tech 3306) writer for IQ captures larger than 4 GB.
  /// SDR# baseband player opens *.rf64; classic RIFF WAV cannot.
  /// </summary>
  public sealed class Rf64WaveWriter : Stream
  {
    private const uint MaxUInt32 = 0xFFFFFFFF;
    private const int Ds64RiffSizeOffset = 20;
    private const int HeaderSize = 80;

    private readonly FileStream stream;
    private readonly int blockAlign;
    private long dataBytes;
    private bool disposed;

    public Rf64WaveWriter(string path, int sampleRate, int channels, int bitsPerSample, bool ieeeFloat)
    {
      if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
      if (channels <= 0) throw new ArgumentOutOfRangeException(nameof(channels));
      if (bitsPerSample <= 0 || bitsPerSample % 8 != 0) throw new ArgumentOutOfRangeException(nameof(bitsPerSample));

      blockAlign = channels * (bitsPerSample / 8);
      int byteRate = sampleRate * blockAlign;
      ushort formatTag = ieeeFloat ? (ushort)3 : (ushort)1;

      stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.Read,
        bufferSize: 4 * 1024 * 1024, FileOptions.SequentialScan);

      // RF64 + WAVE + ds64(28) + fmt(16) + data header = 80 bytes
      WriteFourCc("RF64");
      WriteUInt32(MaxUInt32);
      WriteFourCc("WAVE");

      WriteFourCc("ds64");
      WriteUInt32(28);
      WriteUInt64(0); // riffSize (file length - 8), patched on close
      WriteUInt64(0); // dataSize
      WriteUInt64(0); // sampleCount (frames)
      WriteUInt32(0); // tableLength

      WriteFourCc("fmt ");
      WriteUInt32(16);
      WriteUInt16(formatTag);
      WriteUInt16((ushort)channels);
      WriteUInt32((uint)sampleRate);
      WriteUInt32((uint)byteRate);
      WriteUInt16((ushort)blockAlign);
      WriteUInt16((ushort)bitsPerSample);

      WriteFourCc("data");
      WriteUInt32(MaxUInt32);
    }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => !disposed;
    public override long Length => stream.Length;
    public override long Position
    {
      get => stream.Position;
      set => throw new NotSupportedException();
    }

    public override void Flush() => stream.Flush();

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count)
    {
      ObjectDisposedException.ThrowIf(disposed, this);
      stream.Write(buffer, offset, count);
      dataBytes += count;
    }

    protected override void Dispose(bool disposing)
    {
      if (disposed) return;
      if (disposing)
      {
        PatchSizes();
        stream.Dispose();
      }
      disposed = true;
      base.Dispose(disposing);
    }

    private void PatchSizes()
    {
      stream.Flush();
      long fileLength = stream.Length;
      if (fileLength < HeaderSize) return;

      long riffSize = fileLength - 8;
      ulong sampleCount = blockAlign > 0 ? (ulong)(dataBytes / blockAlign) : 0;

      stream.Position = Ds64RiffSizeOffset;
      WriteUInt64((ulong)riffSize);
      WriteUInt64((ulong)dataBytes);
      WriteUInt64(sampleCount);
      stream.Flush();
    }

    private void WriteFourCc(string id)
    {
      stream.Write(Encoding.ASCII.GetBytes(id));
    }

    private void WriteUInt16(ushort value)
    {
      Span<byte> b = stackalloc byte[2];
      BitConverter.TryWriteBytes(b, value);
      stream.Write(b);
    }

    private void WriteUInt32(uint value)
    {
      Span<byte> b = stackalloc byte[4];
      BitConverter.TryWriteBytes(b, value);
      stream.Write(b);
    }

    private void WriteUInt64(ulong value)
    {
      Span<byte> b = stackalloc byte[8];
      BitConverter.TryWriteBytes(b, value);
      stream.Write(b);
    }
  }
}
