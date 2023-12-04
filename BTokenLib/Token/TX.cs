using System;
using System.Collections.Generic;
using System.Linq;

namespace BTokenLib
{
  public class TX
  {
    public byte[] Hash;

    public List<byte> TXRaw = new();

    public int TXIDShort;

    public bool IsCoinbase;

    public List<TXOutput> TXOutputs = new();

    public long Fee;


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
