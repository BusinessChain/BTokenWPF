using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

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
