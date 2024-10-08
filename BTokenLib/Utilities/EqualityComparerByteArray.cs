﻿using System;
using System.Collections.Generic;


namespace BTokenLib
{
  public class EqualityComparerByteArray : IEqualityComparer<byte[]>
  {
    public bool Equals(byte[] arr1, byte[] arr2)
    {
      return arr1.IsAllBytesEqual(arr2);
    }

    public int GetHashCode(byte[] arr)
    {
      return BitConverter.ToInt32(arr, 0);
    }
  }
}
