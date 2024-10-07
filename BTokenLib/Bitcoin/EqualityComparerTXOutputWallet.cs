using System;
using System.Collections.Generic;


namespace BTokenLib
{
  public class EqualityComparerTXOutputWallet : IEqualityComparer<TXOutputWallet>
  {
    public bool Equals(TXOutputWallet x, TXOutputWallet y)
    {
      return x.TXID.IsAllBytesEqual(y.TXID);
    }

    public int GetHashCode(TXOutputWallet x)
    {
      return BitConverter.ToInt32(x.TXID, 0);
    }
  }
}
