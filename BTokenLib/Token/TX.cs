using System;
using System.IO;
using System.Collections.Generic;


namespace BTokenLib
{
  public abstract class TX
  {
    public byte[] Hash;

    public int CountBytes;

    public long Fee;

    public byte[] TXRaw;

    public bool FlagPrune = true;


    public abstract List<TokenAnchor> GetTokenAnchors();

    public abstract long GetValueOutputs();

    public abstract string Print();

    public void WriteToStream(Stream stream)
    {
      stream.Write(TXRaw, 0, TXRaw.Length);
    }

    public abstract List<(string label, string value)> GetLabelsValuePairs();

    public override string ToString()
    {
      return Hash.ToHexString();
    }

    public abstract bool IsSuccessorTo(TX tX);    
  }
}
