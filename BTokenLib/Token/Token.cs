using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Security.Cryptography;

using LiteDB;


namespace BTokenLib
{
  public abstract partial class Token
  {
    public Network Network;

    public Token TokenParent;

    // Vielleicht wäre es besser auf Childs zu verzichten und immer bottom up zu gehen
    public List<Token> TokensChild = new();

    public long BlockRewardInitial;
    public int PeriodHalveningBlockReward;

    public int SizeBlockMax;

    public TXPool TXPool;
    public FileStream FileTXPoolBackup;
    public List<TX> TXsPoolBackup = new();

    const int COUNT_MAX_BYTES_IN_BLOCK_ARCHIVE = 400_000_000; // Read from configuration file
    const int COUNT_MAX_ACCOUNTS_IN_CACHE = 5_000_000; // Read from configuration file
    const double HYSTERESIS_COUNT_MAX_CACHE_ARCHIV = 0.9;

    public Wallet Wallet;

    public StreamWriter LogFile;
    public ILogEntryNotifier LogEntryNotifier;

    bool IsLocked;
    static object LOCK_Token = new();


    public Token(ILogEntryNotifier logEntryNotifier)
    {
      Directory.CreateDirectory(GetName());

      LogFile = new StreamWriter(Path.Combine(GetName(), "LogToken"), append: false);

      LogEntryNotifier = logEntryNotifier;

      FileTXPoolBackup = new FileStream(
        Path.Combine(GetName(), "FileTXPoolBackup"),
        FileMode.OpenOrCreate,
        FileAccess.ReadWrite,
        FileShare.Read);
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

      lock (LOCK_Token)
      {
        if (IsLocked)
          return false;

        IsLocked = true;
        return true;
      }
    }

    public void ReleaseLock()
    {
      if (TokenParent == null)
        IsLocked = false;
      else
        TokenParent.ReleaseLock();
    }

    public abstract Header CreateHeaderGenesis();

    public void LoadTXPool()
    {
      SHA256 sHA256 = SHA256.Create();

      byte[] fileData = new byte[FileTXPoolBackup.Length];
      FileTXPoolBackup.Read(fileData, 0, (int)FileTXPoolBackup.Length);

      int startIndex = 0;
      while(startIndex < fileData.Length)
      {
        int indexTxStart = startIndex;

        TX tX = ParseTX(fileData, ref startIndex, sHA256);

        tX.TXRaw = new byte[startIndex - indexTxStart];

        Array.Copy(fileData, indexTxStart, tX.TXRaw, 0, tX.TXRaw.Length);

        if (TXPool.TryAddTX(tX))
          Wallet.InsertTXUnconfirmed(tX);
      }

      TokensChild.ForEach(t => t.LoadTXPool());
    }

    public async Task RebroadcastTXsUnconfirmed()
    {
      // Rebroadcast wird bei beendigung der Netzwerk Sync getriggert.
      // Versuche alle txs die noch nicht bestätigt wurden zu rebroadcasten
    }

    public void InsertBlock(Block block)
    {
      $"Insert block {block}.".Log(this, LogEntryNotifier);

      InsertBlockInDatabase(block);

      Wallet.InsertBlock(block);

      TXPool.RemoveTXs(block.TXs.Select(tX => tX.Hash), FileTXPoolBackup);
      TXsPoolBackup.RemoveAll(tXPool => block.TXs.Any(tXBlock => tXPool.Hash.IsAllBytesEqual(tXBlock.Hash)));
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

    public bool IsMining;

    public void StopMining()
    {
      IsMining = false;
    }

    public void StartMining()
    {
      if (IsMining)
        return;

      IsMining = true;

      $"Start {GetName()} miner".Log(this, LogFile, LogEntryNotifier);

      new Thread(RunMining).Start();
    }

    abstract protected void RunMining();

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

    public void InsertTXUnconfirmed(TX tX)
    {
      if (TXPool.TryAddTX(tX))
      {
        tX.WriteToStream(FileTXPoolBackup);
        FileTXPoolBackup.Flush();

        TXsPoolBackup.Add(tX);

        Wallet.InsertTXUnconfirmed(tX);
        Wallet.AddTXUnconfirmedToHistory(tX);
      }
      else
        $"Could not insert tX {tX} to pool.".Log(this, LogEntryNotifier);
    }

    public bool TrySendTX(string address, long value, double feePerByte, out TX tX)
    {
      if (Wallet.TryCreateTX(address, value, feePerByte, out tX))
      {
        InsertTXUnconfirmed(tX);
        Network.AdvertizeTX(tX);
        return true;
      }

      return false;
    }

    public bool TryBroadcastTXData(byte[] data, double feeSatoshiPerByte, int sequence = 0)
    {
      if (Wallet.TryCreateTXData(data, sequence, feeSatoshiPerByte, out TX tX))
      {
        InsertTXUnconfirmed(tX);
        Network.AdvertizeTX(tX);

        $"Created and broadcasted anchor token {tX}.".Log(this, LogEntryNotifier);

        return true;
      }

      return false;
    }
  }
}
