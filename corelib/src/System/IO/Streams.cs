namespace System.IO {
  /* Not implemented natively (managed code has these, on DotNetAnywhere). */
  public enum FileMode { CreateNew = 1, Create = 2, Open = 3, OpenOrCreate = 4, Truncate = 5, Append = 6 }
  public enum FileAccess { Read = 1, Write = 2, ReadWrite = 3 }
  public enum FileShare { None = 0, Read = 1, Write = 2, ReadWrite = 3, Delete = 4 }
  public enum SearchOption { TopDirectoryOnly = 0, AllDirectories = 1 }
  public enum SeekOrigin { Begin = 0, Current = 1, End = 2 }

  public abstract class Stream : IDisposable {
    public abstract bool CanRead { get; }
    public abstract bool CanSeek { get; }
    public abstract bool CanWrite { get; }
    public abstract long Length { get; }
    public abstract long Position { get; set; }
    public abstract void Flush();
    public abstract int Read(byte[] buffer, int offset, int count);
    public abstract long Seek(long offset, SeekOrigin origin);
    public abstract void SetLength(long value);
    public abstract void Write(byte[] buffer, int offset, int count);
    public extern virtual void Close();
    public extern void Dispose();
  }

  public class FileStream : Stream {
    public extern FileStream(string path, FileMode mode);
    public extern FileStream(string path, FileMode mode, FileAccess access);
    public extern FileStream(string path, FileMode mode, FileAccess access, FileShare share);
    public extern override bool CanRead { get; }
    public extern override bool CanSeek { get; }
    public extern override bool CanWrite { get; }
    public extern override long Length { get; }
    public extern override long Position { get; set; }
    public extern override void Flush();
    public extern override int Read(byte[] buffer, int offset, int count);
    public extern override long Seek(long offset, SeekOrigin origin);
    public extern override void SetLength(long value);
    public extern override void Write(byte[] buffer, int offset, int count);
  }

  public abstract class FileSystemInfo {
    public abstract bool Exists { get; }
    public abstract string Name { get; }
    public extern virtual string FullName { get; }
    public extern string Extension { get; }
  }

  public sealed class FileInfo : FileSystemInfo {
    public extern FileInfo(string fileName);
    public extern override bool Exists { get; }
    public extern override string Name { get; }
  }

  public class DirectoryInfo : FileSystemInfo {
    public extern DirectoryInfo(string path);
    public extern override bool Exists { get; }
    public extern override string Name { get; }
    public extern FileInfo[] GetFiles();
    public extern FileInfo[] GetFiles(string searchPattern);
    public extern DirectoryInfo[] GetDirectories();
  }
}

namespace System.IO.Compression {
  public enum CompressionMode { Decompress = 0, Compress = 1 }
  public class GZipStream : Stream {
    public extern GZipStream(Stream stream, CompressionMode mode);
    public extern GZipStream(Stream stream, CompressionMode mode, bool leaveOpen);
    public extern override bool CanRead { get; }
    public extern override bool CanSeek { get; }
    public extern override bool CanWrite { get; }
    public extern override long Length { get; }
    public extern override long Position { get; set; }
    public extern override void Flush();
    public extern override int Read(byte[] buffer, int offset, int count);
    public extern override long Seek(long offset, SeekOrigin origin);
    public extern override void SetLength(long value);
    public extern override void Write(byte[] buffer, int offset, int count);
  }
}

namespace System.Runtime.Serialization {
  public static class FormatterServices {
    public static extern object GetUninitializedObject(Type type);
  }
  public class SerializationException : Exception {
    public SerializationException() {}
    public SerializationException(string message) : base(message) {}
  }
}

namespace System.Runtime.Serialization.Formatters.Binary {
  public sealed class BinaryFormatter {
    public BinaryFormatter() {}
    public extern void Serialize(System.IO.Stream serializationStream, object graph);
    public extern object Deserialize(System.IO.Stream serializationStream);
  }
}
