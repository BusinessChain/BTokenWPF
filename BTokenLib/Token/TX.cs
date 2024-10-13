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


    public abstract bool TryGetAnchorToken(out TokenAnchor tokenAnchor);

    public virtual int GetSequence()
    { throw new NotImplementedException(); }

    public abstract string Print();

    public abstract void WriteToStream(Stream stream);

    public abstract byte[] Serialize();

    public abstract List<(string label, string value)> GetLabelsValuePairs();

    public override string ToString()
    {
      return Hash.ToHexString();
    }

    public abstract bool IsSuccessorTo(TX tX);    

    public abstract bool IsReplacementByFeeFor(TX tX);
  }
}
