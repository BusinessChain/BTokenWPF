using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Security.Cryptography;


namespace BTokenLib
{
  public abstract partial class Token
  {
    public byte[] IDToken;
    public Network Network;

    public Token TokenParent;

    public long BlockRewardInitial;
    public int PeriodHalveningBlockReward;

    public int SizeBlockMax;

    public TXPool TXPool;

    const int COUNT_MAX_BYTES_IN_BLOCK_ARCHIVE = 400_000_000; // Read from configuration file
    const int COUNT_MAX_ACCOUNTS_IN_CACHE = 5_000_000; // Read from configuration file
    const double HYSTERESIS_COUNT_MAX_CACHE_ARCHIV = 0.9;

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

    public void Start()
    {
      $"Start Token {GetName()}".Log(this, LogFile, LogEntryNotifier);

      Network.Start(); // hier soll man erst rauskommen, wenn synchronisiert ist.

      if (TokenParent != null)
        TokenParent.Start();
    }

    public abstract List<string> GetSeedAddresses();

    public void StartSync()
    {
      Token token = this;

      while (token.TokenParent != null)
        token = token.TokenParent;

      token.Network.StartSync();
    }

    public bool TryLock()
    {
      if (TokenParent != null)
        return TokenParent.TryLock();

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
      if (TokenParent != null)
        TokenParent.ReleaseLock();
      else
        IsLocked = false;
    }

    public abstract Header CreateHeaderGenesis();

    public void InsertBlock(Block block)
    {
      $"Insert block {block}.".Log(this, LogEntryNotifier);

      InsertBlockInDatabase(block);

      Wallet.InsertBlock(block);

      // Könnte in InsertBlockInDatabase verschoben werden, damit der derived Pool in der derived class deklariert wird.
      TXPool.RemoveTXs(block.TXs.Select(tX => tX.Hash));
    }

    public bool TryReverseBlock(Block block)
    {
      try
      {
        ReverseBlockInCache(block);

        Wallet.ReverseBlock(block);

        return true;
      }
      catch (ProtocolException ex)
      {
        $"{ex.GetType().Name} when reversing block {block}, height {block.Header.Height} loaded from disk: \n{ex.Message}."
        .Log(this, LogEntryNotifier);

        return false;
      }
    }

    protected virtual void InsertBlockInDatabase(Block block)
    { }

    public virtual void InsertBlockMined(byte[] hashBlock)
    { throw new NotImplementedException(); }

    protected virtual void ReverseBlockInCache(Block block) { }
                  
    public abstract Header ParseHeader(byte[] buffer, ref int index, SHA256 sHA256);

    public TX ParseTX(byte[] tXRaw, SHA256 sHA256)
    {
      int startIndex = 0;

      TX tX = ParseTX(tXRaw, ref startIndex, sHA256);

      tX.TXRaw = tXRaw;

      return tX;
    }

    public abstract TX ParseTX(byte[] buffer, ref int index, SHA256 sHA256);
    
    public abstract TX ParseTXCoinbase(byte[] buffer, ref int index, SHA256 sHA256, long blockReward);

    public virtual void DeleteDB()
    { throw new NotImplementedException(); }

    public virtual List<byte[]> ParseHashesDB(byte[] buffer, int length, Header headerTip)
    { throw new NotImplementedException(); }

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
        TXPool.AddTX(tX);
      }
      finally
      {
        ReleaseLock();
      }

      Wallet.InsertTXUnconfirmed(tX);
    }
  }
}
