using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;


namespace BTokenLib
{
  public partial class Token
  {
    public abstract class TX
    {
      public byte[] Hash;

      public int CountBytes;

      public long Fee;

      public byte[] TXRaw;

      public List<TXOutput> TXOutputs = new();



      public abstract List<TokenAnchor> GetTokenAnchors();

      public long GetValueOutputs()
      {
        return TXOutputs.Sum(t => t.Value);
      }

      public void WriteToStream(Stream stream)
      {
        stream.Write(TXRaw, 0, TXRaw.Length);
      }

      public override string ToString()
      {
        return Hash.ToHexString();
      }

      public abstract bool IsSuccessorTo(TX tX);
    }
  }
}
