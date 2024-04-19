using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;

namespace BTokenLib
{
  public static class VarInt
  {
    public const byte PREFIX_UINT16 = 0XFD;
    public const byte PREFIX_UINT32 = 0XFE;
    public const byte PREFIX_UINT64 = 0XFF;


    public static List<byte> GetBytes(int value)
    {
      return GetBytes((ulong)value);
    }
    
    public static List<byte> GetBytes(ulong value)
    {
      List<byte> serializedValue = new();

      byte prefix;
      int length;
      AssignPrefixAndLength(value, out prefix, out length);

      serializedValue.Add(prefix);
      for (int i = 1; i < length; i++)
      {
        byte nextByte = (byte)(value >> 8 * (i - 1));
        serializedValue.Add(nextByte);
      }

      return serializedValue;
    }
    
    static void AssignPrefixAndLength(
      ulong value, 
      out byte prefix, 
      out int length)
    {
      if (value < PREFIX_UINT16)
      {
        prefix = (byte)value;
        length = 1;
      }
      else if (value <= 0xFFFF)
      {
        prefix = PREFIX_UINT16;
        length = 3;
      }
      else if (value <= 0xFFFFFFFF)
      {
        prefix = PREFIX_UINT32;
        length = 5;
      }
      else
      {
        prefix = PREFIX_UINT64;
        length = 9;
      }
    }
       
    public static int GetInt(byte[] buffer, ref int startIndex)
    {
      int prefix = buffer[startIndex];
      startIndex++;

      if (prefix == PREFIX_UINT16)
      {
        prefix = BitConverter.ToUInt16(buffer, startIndex);
        startIndex += 2;
      }
      else if (prefix == PREFIX_UINT32)
      {
        prefix = BitConverter.ToInt32(buffer, startIndex);
        startIndex += 4;
      }

      return prefix;
    }

    public static int GetInt(Stream stream)
    {
      int prefix = stream.ReadByte();

      if (prefix == PREFIX_UINT16)
      {
        byte[] buffer = new byte[2];
        stream.Read(buffer, 0, 2);
        return BitConverter.ToUInt16(buffer, 0);
      }

      if (prefix == PREFIX_UINT32)
      {
        byte[] buffer = new byte[4];
        stream.Read(buffer, 0, 4);
        return BitConverter.ToInt32(buffer, 0);
      }

      return prefix;
    }
  }
}
