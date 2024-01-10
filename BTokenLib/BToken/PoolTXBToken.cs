using BTokenLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BTokenWPF
{
  public class PoolTXBToken
  {
    readonly object LOCK_TXsPool = new();

    Dictionary<byte[], List<TXBToken>> TXsByIDAccount =
      new(new EqualityComparerByteArray());


    public void RemoveTXs(IEnumerable<byte[]> hashesTX)
    {
    }

    public bool TryAddTX(TXBToken tX)
    {
      lock (LOCK_TXsPool)
      {
        if (TXsByIDAccount.TryGetValue(tX.IDAccount, out List<TXBToken> tXsInPool))
        {
          if (tXsInPool.Last().Nonce + 1 != tX.Nonce
            || tXsInPool.Sum(t => t.Value) + tX.Value > tX.ValueInDB)
            return false;

          tXsInPool.Add(tX);
        }
        else
        {
          if (tX.Nonce != tX.NonceInDB)
            return false;

          TXsByIDAccount.Add(tX.IDAccount, new List<TXBToken> { tX });
        }

        return true;
      }
    }

    public List<TX> GetTXs(out int countTXsPool, int countMax = int.MaxValue)
    {
      countTXsPool = 0;
      return null;
    }

    public bool TryGetTX(byte[] hashTX, out TXBToken tX)
    {
      lock (LOCK_TXsPool)
        return TXPoolDict.TryGetValue(hashTX, out tX);
    }

    public int GetCountTXs()
    {
      lock (LOCK_TXsPool)
        return TXPoolDict.Count;
    }
  }
}
