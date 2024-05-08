using System;
using System.IO;
using System.Text;

namespace BTokenLib
{
  public static class StreamExtensionMethods
  {
    public static void ReadBuffer(this Stream stream, byte[] buffer)
    {
      int offset = 0;
      int bytesToRead = buffer.Length;

      while (bytesToRead > 0)
      {
        int chunkSize = stream.Read( buffer, offset,
          bytesToRead);

        if (chunkSize == 0)
          throw new IOException(
            "Stream returns 0 bytes signifying end of stream.");

        offset += chunkSize;
        bytesToRead -= chunkSize;
      }
    }

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

    public static bool IsEqual(this Stream stream, byte[] arr)
    {
      for (int i = 0; i < arr.Length; i += 1)
        if (arr[i] != stream.ReadByte())
        {
          stream.Position -= i + 1;
          return false;
        }

      return true;
    }
  }
}
