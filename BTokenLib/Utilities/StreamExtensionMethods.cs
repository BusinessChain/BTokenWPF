using System;
using System.IO;

namespace BTokenLib
{
  public static class StreamExtensionMethods
  {
    public static short ReadInt16(this Stream stream)
    {
      byte[] buffer = new byte[2];
      stream.Read(buffer, 0, buffer.Length);

      return BitConverter.ToInt16(buffer);
    }

    public static int ReadInt32(this Stream stream)
    {
      byte[] buffer = new byte[4];
      stream.Read(buffer, 0, buffer.Length);

      return BitConverter.ToInt32(buffer);
    }

    public static long ReadInt64(this Stream stream)
    {
      byte[] buffer = new byte[8];
      stream.Read(buffer, 0, buffer.Length);

      return BitConverter.ToInt64(buffer);
    }
  }
}
