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

      public TXBundle(TXBToken tX)
      {
        IDAccountSource = tX.IDAccountSource;
        FeeAveragePerTX = tX.Fee;
        TXs = new List<TXBToken> { tX };
      }
    }

    readonly object LOCK_TXsPool = new();

    Dictionary<byte[], TXBToken> TXsByHash =
      new(new EqualityComparerByteArray());

    Dictionary<byte[], List<TXBToken>> TXsByIDAccountSource =
      new(new EqualityComparerByteArray());

    Dictionary<byte[], List<TXOutputBToken>> OutputsByIDAccount =
      new(new EqualityComparerByteArray());

    /// <summary>
    /// A bundle conains tXs with the same source account with consecutive nonce.
    /// The miner can pull bundles from this list and be sure to have maximized fee.
    /// </summary>
    List<TXBundle> TXBundlesSortedByFee = new();

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
        accounUnconfirmed.Nonce = tXsInPool.Last().Nonce + 1;
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

      InsertTXInTXBundlesSortedByFee(tX);
    }

    public void RemoveTXs(Block block)
    {
      foreach (TX tXRemove in block.TXs)
      {
        if (!TXsByHash.Remove(tXRemove.Hash, out TXBToken tX))
          continue;

        if(TXsByIDAccountSource.TryGetValue(tX.IDAccountSource, out List<TXBToken> tXs))
        {
          if (!tXs[0].Hash.HasEqualElements(tXRemove.Hash))
            throw new ProtocolException($"Removal of tX {tXRemove} from pool not in expected order of nonce.");
          
          tXs.RemoveAt(0);

          if (tXs.Count == 0)
            TXsByIDAccountSource.Remove(tX.IDAccountSource);
        }

        RebuildTXBundlesSortedByFee();
      }
    }

    void RebuildTXBundlesSortedByFee()
    {
      TXBundlesSortedByFee.Clear();

      foreach(var tXByHash in TXsByHash)
        InsertTXInTXBundlesSortedByFee(tXByHash.Value);
    }

    void InsertTXInTXBundlesSortedByFee(TXBToken tX)
    {
      TXBundle tXBundle = new(tX);

      int i = TXBundlesSortedByFee.Count - 1;

      while(i > -1)
      {
        TXBundle tXBundleNext = TXBundlesSortedByFee[i];

        if(tXBundle.FeeAveragePerTX < tXBundleNext.FeeAveragePerTX)
        {
          TXBundlesSortedByFee.Insert(i + 1, tXBundle);
          return;
        }
        else if(tXBundle.IDAccountSource.HasEqualElements(tXBundleNext.IDAccountSource))
        {
          tXBundleNext.TXs.AddRange(tXBundle.TXs);

          tXBundleNext.FeeAveragePerTX = 
            tXBundleNext.TXs.Sum(t => t.Fee) / tXBundleNext.TXs.Count;

          tXBundle = tXBundleNext;

          TXBundlesSortedByFee.RemoveAt(i);
        }

        i--;
      }

      TXBundlesSortedByFee.Insert(0, tXBundle);
    }

    public List<TXBToken> GetTXs(int countBytesMax)
    {
      List<TXBToken> tXs = new();
      int countBytesCurrent = 0;

      for (int i = 0; i < TXBundlesSortedByFee.Count; i += 1)
        for (int j = 0; j < TXBundlesSortedByFee[i].TXs.Count; j += 1)
        {
          if (countBytesCurrent + TXBundlesSortedByFee[i].TXs[j].TXRaw.Count > countBytesMax)
            return tXs;

          tXs.Add(TXBundlesSortedByFee[i].TXs[j]);
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
