using BTokenLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BTokenWPF
{
  public class PoolTXBToken
  {
    class TXBundle
    {
      public long FeeAveragePerTX;
      public List<TXBToken> TXs = new();
    }

    readonly object LOCK_TXsPool = new();

    Dictionary<byte[], TXBToken> TXsByHash =
      new(new EqualityComparerByteArray());

    Dictionary<byte[], List<TXBToken>> TXsByIDAccount =
      new(new EqualityComparerByteArray());

    List<(byte[] IDAccountSource, TXBundle tXBundle)> TXBundlesSortedByFee = new();


    public void RemoveTXs(IEnumerable<byte[]> hashesTX)
    { }

    public bool TryAddTX(TXBToken tX)
    {
      lock (LOCK_TXsPool)
      {
        if (TXsByIDAccount.TryGetValue(tX.IDAccountSource, out List<TXBToken> tXsInPool))
        {
          if (tXsInPool.Last().Nonce + 1 != tX.Nonce
            || tXsInPool.Sum(t => t.Value) + tX.Value > tX.ValueInDB)
            return false;

          foreach((byte[] IDAccountSource, TXBundle tXBundle) item in TXBundlesSortedByFee)
          {
            if (!tX.IDAccountSource.Equals(item.IDAccountSource))
              continue;

            TXBToken tXLastBundle = item.tXBundle.TXs.Last();
            if (tXLastBundle.Nonce + 1 == tX.Nonce)
            {
              if(tX.Fee > item.tXBundle.FeeAveragePerTX)
            }
          }

          tXsInPool.Add(tX);
        }
        else
        {
          if (tX.Nonce != tX.NonceInDB)
            return false;

          TXsByIDAccount.Add(tX.IDAccountSource, new List<TXBToken> { tX });

          int indexInsert = TXBundlesSortedByFee.FindIndex(b => b.tXBundle.FeeAveragePerTX < tX.Fee);

          if (indexInsert == -1)
            indexInsert = TXBundlesSortedByFee.Count;

          TXBundle tXBundle = new()
          {
            FeeAveragePerTX = tX.Fee,
            TXs = new List<TXBToken> { tX }
          };

          TXBundlesSortedByFee.Insert(indexInsert, (tX.IDAccountSource, tXBundle));
        }

        TXsByHash.Add(tX.Hash, tX);

        return true;
      }
    }

    public List<TX> GetTXs(out int countTXsPool, int countMax)
    {
      countTXsPool = 0;
      return TXsSortedByFee; // es dürfen keine nonces übersprungen werden.
    }

    public bool TryGetTX(byte[] hashTX, out TXBToken tX)
    {
      lock (LOCK_TXsPool)
        return TXsByHash.TryGetValue(hashTX, out tX);
    }

    public int GetCountTXs()
    {
      lock (LOCK_TXsPool)
        return TXsByHash.Count;
    }
  }
}
