using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Collections.Generic;
using System.Security.Cryptography;


namespace BTokenLib
{
  public abstract partial class Token
  {
    public byte[] IDToken;
    public Network Network;

    public int SizeBlockMax;

    public Wallet Wallet;

    public StreamWriter LogFile;
    public ILogEntryNotifier LogEntryNotifier;

    bool IsLocked;


    public Token(ILogEntryNotifier logEntryNotifier)
    {
      Directory.CreateDirectory(GetName());

      LogFile = new StreamWriter(Path.Combine(GetName(), "LogToken"), append: false);

      LogEntryNotifier = logEntryNotifier;
    }

    public virtual void Reset()
    {
      Wallet.Clear();
    }

    public abstract List<string> GetSeedAddresses();

    public bool TryLock()
    {
      lock (this)
      {
        if (IsLocked)
          return false;

        IsLocked = true;
        return true;
      }
    }

    public void ReleaseLock()
    {
        IsLocked = false;
    }

    public abstract Header CreateHeaderGenesis();

    public abstract bool TryGetTX(byte[] hash, out TX tX);

    Dictionary<byte[], TokenAnchor> CacheAnchorTokens = new(new EqualityComparerByteArray());

    public void InsertBlock(Block block)
    {
      try
      {
        for (int i = 0; i < block.TXs.Count; i += 1)
        {
          TX tX = block.TXs[i];

          // Das muss doch im BToken sein, Bitcoin macht das ja ncht
          foreach (TXOutput tXOutput in tX.TXOutputs)
            StageInsertTXOutput(tXOutput, block.Header.Height);

          if (i > 0)
            StageSpendTXInput(tX);

          foreach (TXOutput tXOutput in tX.TXOutputs)
            if (tXOutput is TokenAnchor tokenAnchor)
            {
              if (CacheAnchorTokens.Any(t => t.Value.IDToken.IsAllBytesEqual(tokenAnchor.IDToken)))
                continue;

              CacheAnchorTokens.Add(
                tokenAnchor.HashBlockReferenced,
                tokenAnchor);
            }
        }

        CommitStaged(block);
      }
      finally
      {
        DiscardStaged();
      }

      Wallet?.InsertBlock(block);
    }

    protected virtual void StageSpendTXInput(TX tX) { }
    protected virtual void StageInsertTXOutput(TXOutput output, int blockHeight) { }
    protected virtual void CommitStaged(Block block) { }
    protected virtual void DiscardStaged() { }

    public void ReverseBlock(Block block)
    {
      ReverseBlockInCache(block);

      Wallet.ReverseBlock(block);
    }

    protected virtual void ReverseBlockInCache(Block block) { }

    public abstract Header ParseHeader(byte[] buffer, ref int index, SHA256 sHA256);

    public abstract TX ParseTX(byte[] buffer, ref int index, SHA256 sHA256, bool flagIsCoinbase = false);

    public string GetName()
    {
      return GetType().Name;
    }

    public virtual bool TryGetDB(byte[] hash, out byte[] dataDB)
    { throw new NotImplementedException(); }

    public void BroadcastTX(TX tX)
    {
      InsertTXUnconfirmed(tX);
      Network.BroadcastTX(tX);
    }

    public void InsertTXUnconfirmed(TX tX)
    {
      if (!TryLock())
        throw new SynchronizationLockException("Failed to acquire database lock.");

      try
      {
        AddToTXPool(tX);
        Wallet.InsertTXUnconfirmed(tX);
      }
      finally
      {
        ReleaseLock();
      }
    }

    protected virtual void AddToTXPool(TX tX) { }

  }
}
