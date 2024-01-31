﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace BTokenLib
{
  public abstract class TX
  {
    public byte[] Hash;

    public List<byte> TXRaw = new();

    public bool IsCoinbase;

    public long Fee;

    public List<TXOutput> TXOutputs = new();


    public string GetStringTXRaw()
    {
      return TXRaw.ToArray()
        .Reverse().ToArray()
        .ToHexString();
    }

    public override string ToString()
    {
      return Hash.ToHexString();
    }
  }
}
