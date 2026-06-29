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
    public const byte LENGTH_SCRIPT_P2PKH = 25;
    public static byte[] PREFIX_P2PKH = new byte[] { 0x76, 0xA9, 0x14 };
    public static byte[] POSTFIX_P2PKH = new byte[] { 0x88, 0xAC };

    public byte[] IDToken;
    protected NetworkToken Network;

    public int SizeBlockMax;

    public Wallet WalletToken;

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
      WalletToken.Clear();
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

    public void InsertBlock(Block block)
    {
      InsertBlockInDatabase(block);

      WalletToken?.InsertBlock(block);
    }

    protected virtual void InsertBlockInDatabase(Block block) { }

    public void ReverseBlock(Block block)
    {
      ReverseBlockInCache(block);

      WalletToken.ReverseBlock(block);
    }

    protected virtual void ReverseBlockInCache(Block block) { }

    public abstract Header ParseHeader(byte[] buffer, ref int index, SHA256 sHA256);

    public abstract TX ParseTX(byte[] buffer, ref int index, SHA256 sHA256, bool flagIsCoinbase = false);

    public string GetName()
    {
      return GetType().Name;
    }


    const int COUNT_BYTES_PER_BLOCK_MAX = 1000;
    const int TIMESPAN_MINING_ANCHOR_TOKENS_SECONDS = 4;
    const int TIME_MINER_PAUSE_AFTER_RECEIVE_PARENT_BLOCK_SECONDS = 5;
    const double FACTOR_INCREMENT_FEE_PER_BYTE_ANCHOR_TOKEN = 1.02;
    const double MINIMUM_FEE_SATOSHI_PER_BYTE_ANCHOR_TOKEN = 0.1;
    
    Block MineBlock(out byte[] dataAnchorToken)
    {
      Block block = new Block(this);

      LoadTXsFromPool(block, out long feeTXs);

      int height = NetworkToken.HeaderTip.Height + 1;

      long blockReward = BLOCK_REWARD_INITIAL >> height / PERIOD_HALVENING_BLOCK_REWARD;
      blockReward += feeTXs;

      TX tXCoinbase = ((WalletBToken)Wallet).CreateTXCoinbase(blockReward, height);

      block.TXs.Insert(0, tXCoinbase);

      block.Header = new HeaderBToken()
      {
        HashPrevious = NetworkToken.HeaderTip.Hash,
        HeaderPrevious = NetworkToken.HeaderTip,
        Height = height,
        MerkleRoot = block.ComputeMerkleRoot(),
        CountTXs = block.TXs.Count,
        Fee = feeTXs
      };

      block.Header.ComputeHash();

      block.Serialize();

      anchorToken = IDENTIFIER_BTOKEN_PROTOCOL
      .Concat(IDToken)
      .Concat(block.Header.Hash)
      .Concat(block.Header.HashPrevious).ToArray();

      return block;
    }

    public virtual void LoadTXsFromPool(Block block, out long feeTXs)
    {
      feeTXs = 0;
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
        WalletToken.InsertTXUnconfirmed(tX);
      }
      finally
      {
        ReleaseLock();
      }
    }

    protected virtual void AddToTXPool(TX tX) { }

  }
}
