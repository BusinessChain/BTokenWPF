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

    Dictionary<byte[], List<TXBToken>> TXsByIDAccountSource =
      new(new EqualityComparerByteArray());

    Dictionary<byte[], List<TXOutputBToken>> OutputsByIDAccount =
      new(new EqualityComparerByteArray());

    List<TXBundle> TXBundlesSortedByFee = new();


    public void RemoveTXs(IEnumerable<byte[]> hashesTX)
    {
      foreach (byte[] hashTX in hashesTX)
      {
        if (!TXsByHash.Remove(hashTX, out TXBToken tX))
          continue;

        TXsByIDAccountSource.TryGetValue(tX.IDAccountSource, out List<TXBToken> tXs);
        tXs.RemoveAt(0);

        if (tXs.Count == 0)
          TXsByIDAccountSource.Remove(tX.IDAccountSource);

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

    public void AddTX(TXBToken tX, Account accountScource)
    {
      lock (LOCK_TXsPool)
      {
        long valueAccountNetPool = accountScource.Value;

        if (TXsByIDAccountSource.TryGetValue(tX.IDAccountSource, out List<TXBToken> tXsInPool))
        {
          valueAccountNetPool -= tXsInPool.Sum(t => t.Value);

          if(tXsInPool.Last().Nonce + 1 != tX.Nonce)
            throw new ProtocolException($"Nonce {tX.Nonce} of tX {tX} not in succession with nonce {tXsInPool.Last().Nonce} of last tX in pool.");
        }
        else if (accountScource.Nonce != tX.Nonce)
          throw new ProtocolException($"Nonce {tX.Nonce} of tX {tX} not equal to nonce {accountScource.Nonce} of account {accountScource}.");

        if (OutputsByIDAccount.TryGetValue(tX.IDAccountSource, out List<TXOutputBToken> outputsInPool))
          valueAccountNetPool += outputsInPool.Sum(o => o.Value);

        if (valueAccountNetPool < tX.Value)
          throw new ProtocolException($"Value {tX.Value} of tX {tX} bigger than value {valueAccountNetPool} in account {accountScource} considering tXs in pool.");

        InsertTX(tX);
      }
    }

    public Account ApplyTXsOnAccount(Account account)
    {
      Account accounUnconfirmed = new();

      accounUnconfirmed.BlockheightAccountInit = account.BlockheightAccountInit;
      accounUnconfirmed.Nonce = account.Nonce;
      accounUnconfirmed.IDAccount = account.IDAccount;
      accounUnconfirmed.Value = account.Value;

      if (TXsByIDAccountSource.TryGetValue(account.IDAccount, out List<TXBToken> tXsInPool))
      {
        accounUnconfirmed.Nonce = tXsInPool.Last().Nonce;
        accounUnconfirmed.Value -= tXsInPool.Sum(t => t.Value);
      }

      if (OutputsByIDAccount.TryGetValue(account.IDAccount, out List<TXOutputBToken> outputsInPool))
        accounUnconfirmed.Value += outputsInPool.Sum(o => o.Value);

      return accounUnconfirmed;
    }

    void InsertTX(TXBToken tX)
    {
      TXsByHash.Add(tX.Hash, tX);

      if (TXsByIDAccountSource.TryGetValue(tX.IDAccountSource, out List<TXBToken> tXsInPool))
        tXsInPool.Add(tX);
      else
        TXsByIDAccountSource.Add(tX.IDAccountSource, new List<TXBToken>() { tX });

      TXBTokenValueTransfer tXValueTransfer = tX as TXBTokenValueTransfer;

      if (tXValueTransfer != null)
        foreach (TXOutputBToken tXOutputBToken in tXValueTransfer.TXOutputs)
          if (OutputsByIDAccount.TryGetValue(tXOutputBToken.IDAccount, out List<TXOutputBToken> tXOutputsBToken))
            tXOutputsBToken.Add(tXOutputBToken);
          else
            OutputsByIDAccount.Add(tXOutputBToken.IDAccount, new List<TXOutputBToken>() { tXOutputBToken });

      int i = 0;
      while (true)
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

      InsertTXBundle(new()
      {
        IDAccountSource = tX.IDAccountSource,
        FeeAveragePerTX = tX.Fee,
        TXs = new List<TXBToken> { tX }
      });
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
