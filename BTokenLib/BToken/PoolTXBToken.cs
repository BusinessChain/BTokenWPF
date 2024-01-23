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
      public byte[] IDAccountSource;
      public long FeeAveragePerTX;
      public List<TXBToken> TXs = new();
    }

    readonly object LOCK_TXsPool = new();

    Dictionary<byte[], TXBToken> TXsByHash =
      new(new EqualityComparerByteArray());

    Dictionary<byte[], List<TXBToken>> TXsByIDAccount =
      new(new EqualityComparerByteArray());

    List<TXBundle> TXBundlesSortedByFee = new();


    public void RemoveTXs(IEnumerable<byte[]> hashesTX)
    {
      foreach (byte[] hashTX in hashesTX)
      {
        if (!TXsByHash.Remove(hashTX, out TXBToken tX))
          continue;

        TXsByIDAccount.TryGetValue(tX.IDAccountSource, out List<TXBToken> tXs);
        tXs.RemoveAt(0);

        if (tXs.Count == 0)
          TXsByIDAccount.Remove(tX.IDAccountSource);

        int indexBundle = TXBundlesSortedByFee.FindIndex(b => b.IDAccountSource.Equals(tX.IDAccountSource));
        TXBundle tXBundle = TXBundlesSortedByFee[indexBundle];
        TXBundlesSortedByFee.RemoveAt(indexBundle);

        tXBundle.TXs.RemoveAt(0);

        if (tXBundle.TXs.Count > 0)
        {
          tXBundle.FeeAveragePerTX = tXBundle.TXs.Sum(t => t.Fee) / tXBundle.TXs.Count;
          InsertTXBundle(tXBundle);
        }
      }
    }

    void InsertTXBundle(TXBundle tXBundle)
    {
      int indexInsert = TXBundlesSortedByFee.FindIndex(b => b.FeeAveragePerTX < tXBundle.FeeAveragePerTX);

      if (indexInsert == -1)
        indexInsert = TXBundlesSortedByFee.Count;

      TXBundlesSortedByFee.Insert(indexInsert, tXBundle);
    }

    public bool TryAddTX(TXBToken tX)
    {
      lock (LOCK_TXsPool)
      {
        if (TXsByIDAccount.TryGetValue(tX.IDAccountSource, out List<TXBToken> tXsInPool))
        {
          if (tXsInPool.Last().Nonce + 1 != tX.Nonce
            || tXsInPool.Sum(t => t.Value) + tX.Value > tX.ValueInDB)
            return false;

          int i = 0;
          while(true)
          {
            TXBundle tXBundle = TXBundlesSortedByFee[i];

            if (!tX.IDAccountSource.Equals(tXBundle.IDAccountSource) ||
              tX.Nonce > tXBundle.TXs.Last().Nonce + 1)
            {
              i += 1;
              continue;
            }

            if (tX.Fee < tXBundle.FeeAveragePerTX)
              InsertTXBundle(new()
              {
                IDAccountSource = tX.IDAccountSource,
                FeeAveragePerTX = tX.Fee,
                TXs = new List<TXBToken> { tX }
              });
            else
            {
              tXBundle.TXs.Add(tX);

              if (tX.Fee > tXBundle.FeeAveragePerTX)
              {
                tXBundle.FeeAveragePerTX = tXBundle.TXs.Sum(t => t.Fee) / tXBundle.TXs.Count;
                TXBundlesSortedByFee.RemoveAt(i);

                InsertTXBundle(tXBundle);
              }

              break;
            }
          }

          tXsInPool.Add(tX);
        }
        else
        {
          if (tX.Nonce != tX.NonceInDB)
            return false;

          TXsByIDAccount.Add(tX.IDAccountSource, new List<TXBToken> { tX });

          InsertTXBundle(new()
          {
            IDAccountSource = tX.IDAccountSource,
            FeeAveragePerTX = tX.Fee,
            TXs = new List<TXBToken> { tX }
          });
        }

        TXsByHash.Add(tX.Hash, tX);

        return true;
      }
    }

    public List<TXBToken> GetTXs(int countMax)
    {
      List<TXBToken> tXs = new();

      int i = 0;
      while (i < TXBundlesSortedByFee.Count)
      {
        TXBundle tXBundle = TXBundlesSortedByFee[i];

        if (tXs.Count + tXBundle.TXs.Count > countMax)
          break;

        tXs.AddRange(tXBundle.TXs);

        i += 1;
      }
      
      return tXs;
    }

    public bool TryGetTX(byte[] hashTX, out TXBToken tX)
    {
      lock (LOCK_TXsPool)
        return TXsByHash.TryGetValue(hashTX, out tX);
    }
  }
}
