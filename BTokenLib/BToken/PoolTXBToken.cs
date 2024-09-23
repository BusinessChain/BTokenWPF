using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;


namespace BTokenLib
{
  public class PoolTXBToken : TXPool
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


    public PoolTXBToken(Token token)
      : base(token)
    { }

    public override bool TryAddTX(TX tX)
    {
      try
      {
        TXBToken tXBToken = (TXBToken)tX;

        if (!((TokenBToken)Token).DBAccounts.TryGetAccount(tXBToken.IDAccountSource, out Account accountSource))
          throw new ProtocolException($"Account source {tXBToken.IDAccountSource} referenced by {tX} not in database.");

        if (accountSource.BlockheightAccountInit != tXBToken.BlockheightAccountInit)
          throw new ProtocolException($"BlockheightAccountInit {tXBToken.BlockheightAccountInit} as specified in tX {tX} not equal as in account {accountSource} where it is {accountSource.BlockheightAccountInit}.");

        lock (LOCK_TXsPool)
        {
          long valueAccountNetPool = accountSource.Value;

          if (TXsByIDAccountSource.TryGetValue(tXBToken.IDAccountSource, out List<TXBToken> tXsInPool))
          {
            valueAccountNetPool -= tXsInPool.Sum(t => t.Value);

            if (tXsInPool.Last().Nonce + 1 != tXBToken.Nonce)
              throw new ProtocolException($"Nonce {tXBToken.Nonce} of tX {tXBToken} not in succession with nonce {tXsInPool.Last().Nonce} of last tX in pool.");
          }
          else if (accountSource.Nonce != tXBToken.Nonce)
            throw new ProtocolException($"Nonce {tXBToken.Nonce} of tX {tXBToken} not equal to nonce {accountSource.Nonce} of account {accountSource}.");

          if (OutputsByIDAccount.TryGetValue(tXBToken.IDAccountSource, out List<TXOutputBToken> outputsInPool))
            valueAccountNetPool += outputsInPool.Sum(o => o.Value);

          if (valueAccountNetPool < tXBToken.Value)
            throw new ProtocolException($"Value {tXBToken.Value} of tX {tXBToken} bigger than value {valueAccountNetPool} in account {accountSource} considering tXs in pool.");

          InsertTX(tXBToken);
        }
        return true;
      }
      catch (ProtocolException ex)
      {
        ex.Message.Log(this, Token.LogEntryNotifier);
        return false;
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

    public override void RemoveTXs(IEnumerable<byte[]> hashesTX, FileStream fileTXPoolBackup)
    {
      foreach (byte[] hashTX in hashesTX)
      {
        if (!TXsByHash.Remove(hashTX, out TXBToken tX))
          continue;

        if(TXsByIDAccountSource.TryGetValue(tX.IDAccountSource, out List<TXBToken> tXsByAccountSource))
        {
          if (!tXsByAccountSource[0].Hash.IsAllBytesEqual(hashTX))
            throw new ProtocolException($"Removal of tX {hashTX.ToHexString()} from pool not in expected order of nonce.");

          tXsByAccountSource.RemoveAt(0);

          if (tXsByAccountSource.Count == 0)
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
        else if(tXBundle.IDAccountSource.IsAllBytesEqual(tXBundleNext.IDAccountSource))
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

    public override List<TX> GetTXs(int countBytesMax, out long feeTotal)
    {
      List<TX> tXs = new();
      int countBytesCurrent = 0;
      feeTotal = 0;

      for (int i = 0; i < TXBundlesSortedByFee.Count; i += 1)
        for (int j = 0; j < TXBundlesSortedByFee[i].TXs.Count; j += 1)
        {
          if (countBytesCurrent + TXBundlesSortedByFee[i].TXs[j].TXRaw.Count > countBytesMax)
            return tXs;

          tXs.Add(TXBundlesSortedByFee[i].TXs[j]);
          feeTotal += TXBundlesSortedByFee[i].TXs[j].Fee;
        }

      return tXs;
    }

    public override bool TryGetTX(byte[] hashTX, out TX tX)
    {
      lock (LOCK_TXsPool)
      {
        if (TXsByHash.TryGetValue(hashTX, out TXBToken tXBToken))
        {
          tX = tXBToken;
          return true;
        }

        tX = null;
        return false;
      }
    }
  }
}
