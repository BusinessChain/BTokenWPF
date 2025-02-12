﻿using System;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace BTokenLib
{
  public static class ByteArrayExtensionMethods
  {
    static readonly string[] BYTE2HEX = new string[] 
    { 
      "00", "01", "02", "03", "04", "05", "06", "07", "08", "09", "0A", "0B", "0C", "0D", "0E", "0F",
      "10", "11", "12", "13", "14", "15", "16", "17", "18", "19", "1A", "1B", "1C", "1D", "1E", "1F",
      "20", "21", "22", "23", "24", "25", "26", "27", "28", "29", "2A", "2B", "2C", "2D", "2E", "2F",
      "30", "31", "32", "33", "34", "35", "36", "37", "38", "39", "3A", "3B", "3C", "3D", "3E", "3F",
      "40", "41", "42", "43", "44", "45", "46", "47", "48", "49", "4A", "4B", "4C", "4D", "4E", "4F",
      "50", "51", "52", "53", "54", "55", "56", "57", "58", "59", "5A", "5B", "5C", "5D", "5E", "5F",
      "60", "61", "62", "63", "64", "65", "66", "67", "68", "69", "6A", "6B", "6C", "6D", "6E", "6F",
      "70", "71", "72", "73", "74", "75", "76", "77", "78", "79", "7A", "7B", "7C", "7D", "7E", "7F",
      "80", "81", "82", "83", "84", "85", "86", "87", "88", "89", "8A", "8B", "8C", "8D", "8E", "8F",
      "90", "91", "92", "93", "94", "95", "96", "97", "98", "99", "9A", "9B", "9C", "9D", "9E", "9F",
      "A0", "A1", "A2", "A3", "A4", "A5", "A6", "A7", "A8", "A9", "AA", "AB", "AC", "AD", "AE", "AF",
      "B0", "B1", "B2", "B3", "B4", "B5", "B6", "B7", "B8", "B9", "BA", "BB", "BC", "BD", "BE", "BF",
      "C0", "C1", "C2", "C3", "C4", "C5", "C6", "C7", "C8", "C9", "CA", "CB", "CC", "CD", "CE", "CF",
      "D0", "D1", "D2", "D3", "D4", "D5", "D6", "D7", "D8", "D9", "DA", "DB", "DC", "DD", "DE", "DF",
      "E0", "E1", "E2", "E3", "E4", "E5", "E6", "E7", "E8", "E9", "EA", "EB", "EC", "ED", "EE", "EF",
      "F0", "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "FA", "FB", "FC", "FD", "FE", "FF"
    };

    public static string ToHexString(this byte[] array)
    {
      if (array == null)
        return "";

      string[] stringArrayHex = new string[array.Length];

      for (int i = 0; i < array.Length; i++)
        stringArrayHex[array.Length - i - 1] = BYTE2HEX[array[i]];

      return string.Join(
        separator: null, 
        stringArrayHex);
    }

    public static string ToHexString(this List<byte> list)
    {
      return ToHexString(list.ToArray());
    }

    public static string BinaryToBase58Check(this byte[] byteArray)
    {
      List<byte> pubKey = byteArray.ToList();
      pubKey.Insert(0, 0x00);

      byte[] checksum = SHA256.HashData(SHA256.HashData(pubKey.ToArray()));

      pubKey.AddRange(checksum.Take(4));

      byte[] ba = pubKey.ToArray();

      Org.BouncyCastle.Math.BigInteger addrremain = new(1, ba);
      Org.BouncyCastle.Math.BigInteger big0 = new("0");
      Org.BouncyCastle.Math.BigInteger big58 = new("58");

      string b58 = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";

      string rv = "";

      while (addrremain.CompareTo(big0) > 0)
      {
        int d = Convert.ToInt32(addrremain.Mod(big58).ToString());
        addrremain = addrremain.Divide(big58);
        rv = b58.Substring(d, 1) + rv;
      }

      foreach (byte b in ba)
      {
        if (b != 0) break;
        rv = "1" + rv;
      }

      return rv;
    }

    public static bool IsAllBytesEqual(this byte[] arr1, byte[] arr2, int startIndex2 = 0)
    {
      for (int i = 0; i < arr1.Length; i++)
        if (arr1[i] != arr2[startIndex2 + i])
          return false;

      return true;
    }

    public static bool IsGreaterThan(this byte[] array, uint nBits)
    {
      int expBits = ((int)nBits & 0x7F000000) >> 24;
      UInt32 factorBits = nBits & 0x00FFFFFF;

      if (expBits < 3)
        factorBits >>= (3 - expBits) * 8;

      var bytes = new List<byte>();

      for (int i = expBits - 3; i > 0; i--)
        bytes.Add(0x00);

      bytes.Add((byte)(factorBits & 0xFF));
      bytes.Add((byte)((factorBits & 0xFF00) >> 8));
      bytes.Add((byte)((factorBits & 0xFF0000) >> 16));

      return array.IsGreaterThan(bytes.ToArray());
    }

    public static bool IsGreaterThan(this byte[] a1, byte[] a2)
    {    
      int i = a1.Length;

      while(i-- > a2.Length)
        if (a1[i] > 0)
          return true;

      while (a1[i] == a2[i])
      {
        i -= 1;

        if (i < 0)
          return false;
      }  

      return a1[i] > a2[i];
    }

    public static void Increment(this byte[] array, int startIndex, int length)
    {
      int offset = 0;
      int index;

      while (offset < length)
      {
        index = startIndex + offset;

        array[index] += 1;

        if (array[index] != 0) 
          return;

        offset += 1;
      }
    }
    
    public static byte[] SubtractByteWise(this byte[] array1, byte[] array2)
    {
      byte[] difference = new byte[array1.Length];

      for (int i = 0; i < array1.Length; i++)
        difference[i] = (byte)(array1[i] - array2[i]);

      return difference;
    }
  }
}
