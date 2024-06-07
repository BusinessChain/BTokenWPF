using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace BTokenLib
{
  public abstract class TX
  {
    public byte[] Hash;

    public List<byte> TXRaw = new();


    public string GetStringTXRaw()
    {
      return TXRaw.ToArray()
        .Reverse().ToArray()
        .ToHexString();
    }

    public abstract string Print();

    public abstract void WriteToStream(Stream stream);

    public abstract List<(string label, string value)> GetLabelsValuePairs();

    public override string ToString()
    {
      return Hash.ToHexString();
    }

    public abstract bool IsSuccessorTo(TX tX);
  }
}
