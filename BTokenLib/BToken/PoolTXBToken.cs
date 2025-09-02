using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;


namespace BTokenLib
{
  public partial class TokenBToken : Token
  {
    public class PoolTXBToken : TXPool
    {
      class TXBundle
      {
        public byte[] IDAccountSource;
        public long FeeAverageTX;
        public List<TXBToken> TXs = new();

        public TXBundle(TXBToken tX)
        {
          IDAccountSource = tX.IDAccountSource;
          FeeAverageTX = tX.Fee;
          TXs = new List<TXBToken> { tX };
        }
      }

      TokenBToken Token;

      readonly object LOCK_TXsPool = new();

      int SequenceNumberTX;

      Dictionary<byte[], (TXBToken tX, int sequenceNumberTX)> TXsByHash = new(new EqualityComparerByteArray());

      Dictionary<byte[], List<TXBToken>> TXsByIDAccountSource = new(new EqualityComparerByteArray());

      Dictionary<byte[], long> OutputValuesByIDAccount = new(new EqualityComparerByteArray());

      /// <summary>
      /// A bundle conains tXs with the same source account with consecutive nonce.
      /// The miner can pull bundles from this list and be sure to have maximized fee.
      /// </summary>
      List<TXBundle> TXBundlesSortedByFee = new();


      public PoolTXBToken(TokenBToken token)
      {
        Token = token;
      }

      public override bool TryAddTX(TX tX)
      {
        try
        {
          TXBToken tXBToken = tX as TXBToken;

          if (!Token.DBAccounts.TryGetAccount(tXBToken.IDAccountSource, out Account accountSource))
            throw new ProtocolException($"Account source {tXBToken.IDAccountSource} referenced by {tX} not in database.");

          if (accountSource.BlockHeightAccountCreated != tXBToken.BlockheightAccountInit)
            throw new ProtocolException($"BlockheightAccountInit {tXBToken.BlockheightAccountInit} as specified in tX {tX} not equal as in account {accountSource} where it is {accountSource.BlockHeightAccountCreated}.");

          lock (LOCK_TXsPool)
          {
            long valueAccountNetPool = accountSource.Balance;

            if (TXsByIDAccountSource.TryGetValue(tXBToken.IDAccountSource, out List<TXBToken> tXsInPool))
            {
              valueAccountNetPool -= tXsInPool.Sum(t => t.GetValueOutputs() + t.Fee);

              if (tXsInPool.Last().Nonce + 1 != tXBToken.Nonce)
                throw new ProtocolException($"Nonce {tXBToken.Nonce} of tX {tXBToken} not in succession with nonce {tXsInPool.Last().Nonce} of last tX in pool.");
            }
            else if (accountSource.Nonce != tXBToken.Nonce)
              throw new ProtocolException($"Nonce {tXBToken.Nonce} of tX {tXBToken} not equal to nonce {accountSource.Nonce} of account {accountSource}.");

            if (OutputValuesByIDAccount.TryGetValue(tXBToken.IDAccountSource, out long outputValue))
              valueAccountNetPool += outputValue;

            if (valueAccountNetPool < tXBToken.GetValueOutputs() + tXBToken.Fee)
              throw new ProtocolException($"Value {tXBToken.GetValueOutputs() + tXBToken.Fee} of tX {tXBToken} bigger than value {valueAccountNetPool} in account {accountSource} considering tXs in pool.");

            TXsByHash.Add(tXBToken.Hash, (tXBToken, SequenceNumberTX++));

            if (tXsInPool == null)
              TXsByIDAccountSource.Add(tXBToken.IDAccountSource, new List<TXBToken>() { tXBToken });
            else
              tXsInPool.Add(tXBToken);

            if (tXBToken is TXBTokenValueTransfer tXValueTransfer)
              foreach (TXOutputBToken tXOutputBToken in tXValueTransfer.TXOutputs)
                if (!OutputValuesByIDAccount.TryAdd(tXOutputBToken.IDAccount, tXOutputBToken.Value))
                  OutputValuesByIDAccount[tXOutputBToken.IDAccount] += tXOutputBToken.Value;

            InsertTXInTXBundlesSortedByFee(tXBToken);
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

        accounUnconfirmed.BlockHeightAccountCreated = account.BlockHeightAccountCreated;
        accounUnconfirmed.Nonce = account.Nonce;
        accounUnconfirmed.ID = account.ID;
        accounUnconfirmed.Balance = account.Balance;

        if (TXsByIDAccountSource.TryGetValue(account.ID, out List<TXBToken> tXsInPool))
        {
          accounUnconfirmed.Nonce = tXsInPool.Last().Nonce + 1;
          accounUnconfirmed.Balance -= tXsInPool.Sum(t => t.GetValueOutputs() + t.Fee);
        }

        if (OutputValuesByIDAccount.TryGetValue(account.ID, out long valueOutputs))
          accounUnconfirmed.Balance += valueOutputs;

        return accounUnconfirmed;
      }

      public override void RemoveTXs(IEnumerable<byte[]> hashesTX, FileStream fileTXPoolBackup)
      {
        foreach (byte[] hashTX in hashesTX)
        {
          if (!TXsByHash.Remove(hashTX, out (TXBToken tX, int sequenceNumberTX) tXsByHashItem))
            continue;

          TXBToken tX = tXsByHashItem.tX;

          List<TXBToken> tXsByAccountSource = TXsByIDAccountSource[tX.IDAccountSource];

          if (!tXsByAccountSource[0].Hash.IsAllBytesEqual(hashTX))
            throw new ProtocolException($"Removal of tX {hashTX.ToHexString()} from pool not in expected order of nonce.");

          tXsByAccountSource.RemoveAt(0);

          if (tXsByAccountSource.Count == 0)
            TXsByIDAccountSource.Remove(tX.IDAccountSource);

          if (tX is TXBTokenValueTransfer tXValueTransfer)
            foreach (TXOutputBToken tXOutput in tXValueTransfer.TXOutputs)
            {
              OutputValuesByIDAccount[tXOutput.IDAccount] -= tXOutput.Value;

              if (OutputValuesByIDAccount[tXOutput.IDAccount] == 0)
                OutputValuesByIDAccount.Remove(tXOutput.IDAccount);
            }

          TXBundlesSortedByFee.Clear();
          fileTXPoolBackup.SetLength(0);
          SequenceNumberTX = 0;

          var orderedItems = TXsByHash.OrderBy(i => i.Value.sequenceNumberTX).ToList();

          for (int i = 0; i < orderedItems.Count; i++)
          {
            TXsByHash[orderedItems[i].Key] = (orderedItems[i].Value.tX, SequenceNumberTX++);
            InsertTXInTXBundlesSortedByFee(orderedItems[i].Value.tX);
            orderedItems[i].Value.tX.WriteToStream(fileTXPoolBackup);
          }

          fileTXPoolBackup.Flush();
        }
      }

      void InsertTXInTXBundlesSortedByFee(TXBToken tX)
      {
        TXBundle tXBundle = new(tX);

        int i = TXBundlesSortedByFee.Count - 1;

        while (i > -1)
        {
          TXBundle tXBundleNext = TXBundlesSortedByFee[i];

          if (tXBundle.FeeAverageTX < tXBundleNext.FeeAverageTX)
          {
            TXBundlesSortedByFee.Insert(i + 1, tXBundle);
            return;
          }
          else if (tXBundle.IDAccountSource.IsAllBytesEqual(tXBundleNext.IDAccountSource))
          {
            tXBundleNext.TXs.AddRange(tXBundle.TXs);

            tXBundleNext.FeeAverageTX =
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

        for (int i = 0; i < TXBundlesSortedByFee.Count; i++)
          for (int j = 0; j < TXBundlesSortedByFee[i].TXs.Count; j++)
          {
            if (countBytesCurrent + TXBundlesSortedByFee[i].TXs[j].CountBytes > countBytesMax)
              return tXs;

            tXs.Add(TXBundlesSortedByFee[i].TXs[j]);

            countBytesCurrent += TXBundlesSortedByFee[i].TXs[j].CountBytes;
            feeTotal += TXBundlesSortedByFee[i].TXs[j].Fee;
          }

        return tXs;
      }

      public override bool TryGetTX(byte[] hashTX, out TX tX)
      {
        lock (LOCK_TXsPool)
        {
          if (TXsByHash.TryGetValue(hashTX, out (TXBToken tX, int) itemTXPool))
          {
            tX = itemTXPool.tX;
            return true;
          }

          tX = null;
          return false;
        }
      }

      public override void Clear()
      {
        TXsByHash.Clear();
        TXsByIDAccountSource.Clear();
        OutputValuesByIDAccount.Clear();
      }
    }
  }
}
