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
  public partial class TokenBToken : Token
  {
    const int SIZE_BLOCK_MAX = 1 << 22; // 4 MB

    const long BLOCK_REWARD_INITIAL = 200000000000000; // 200 BTK
    const int PERIOD_HALVENING_BLOCK_REWARD = 105000;

    const long COUNT_SATOSHIS_PER_DAY_MINING = 500000;
    const long TIMESPAN_DAY_SECONDS = 24 * 3600;

    public enum TypesToken
    {
      Coinbase = 0,
      ValueTransfer = 1,
      AnchorToken = 2,
      Data = 3
    }

    Dictionary<byte[], Account> Cache = new(new EqualityComparerByteArray());
    Dictionary<byte[], Account> AccountsStaged = new(new EqualityComparerByteArray());

    LiteDatabase Database;
    ILiteCollection<Account> DatabaseAccountCollection;
    ILiteCollection<BsonDocument> DatabaseMetaCollection;

    string PathRootDB;
    public const int COUNT_FILES_DB = 256;
    byte[] HashesFilesDB = new byte[COUNT_FILES_DB * 32];
    const int COUNT_MAX_ACCOUNTS_IN_CACHE = 5_000_000; // Read from configuration file
    const int COUNT_EVICTION_ACCOUNTS_FROM_CACHE = 200_000; // Read from configuration file
    const double HYSTERESIS_COUNT_MAX_CACHE_ARCHIV = 0.9;

    const int LENGTH_TX_P2PKH = 120;
    const int LENGTH_TX_DATA_SCAFFOLD = 30;


    public TokenBToken(ILogEntryNotifier logEntryNotifier)
      : base(logEntryNotifier)
    {
      Wallet = new WalletBToken(File.ReadAllText($"Wallet{GetName()}/wallet"), this);

      TXPool = new PoolTXBToken(this);

      PathBlocksMined = Path.Combine(GetName(), "blocksMined");
      Directory.CreateDirectory(PathBlocksMined);

      SizeBlockMax = SIZE_BLOCK_MAX;

      BlockRewardInitial = BLOCK_REWARD_INITIAL;
      PeriodHalveningBlockReward = PERIOD_HALVENING_BLOCK_REWARD;

      DatabaseAccountCollection = Database.GetCollection<Account>("accounts");
      DatabaseMetaCollection = Database.GetCollection<BsonDocument>("meta");

      AppDomain.CurrentDomain.ProcessExit += (s, e) => { Database?.Dispose(); };

      TokenParent = new TokenBitcoin(logEntryNotifier);

      IDToken = new byte[3] { (byte)'B', (byte)'T', (byte)'K' };

      Network = new Network(
        this,
        port: 8777,
        flagEnableInboundConnections: true);
    }

    public override Header CreateHeaderGenesis()
    {
      HeaderBToken header = new(
        headerHash: "000000000019d6689c085ae165831e934ff763ae46a2a6c172b3f1b60a8ce26f".ToBinary(),
        hashPrevious: "0000000000000000000000000000000000000000000000000000000000000000".ToBinary(),
        merkleRootHash: "4a5e1e4baab89f3a32518a88c31bc87f618f76673e2cc77ab2127b7afdeda33b".ToBinary(),
        hashDatabase: "0000000000000000000000000000000000000000000000000000000000000000".ToBinary(),
        unixTimeSeconds: 1231006505,
        nonce: 0);

      return header;
    }

    public override TX ParseTXCoinbase(byte[] buffer, ref int startIndex, SHA256 sHA256, long blockReward)
    {
      TypesToken typeToken = (TypesToken)buffer[startIndex];
      startIndex += 1;

      if (typeToken != TypesToken.Coinbase)
        throw new ProtocolException($"First tX of block is not of type coinbase.");

      return new TXBTokenCoinbase(buffer, ref startIndex, sHA256);
    }

    public override TX ParseTX(byte[] buffer, ref int startIndex, SHA256 sHA256)
    {
      TypesToken typeToken = (TypesToken)buffer[startIndex];
      startIndex += 1;

      if (typeToken == TypesToken.ValueTransfer)
        return new TXBTokenValueTransfer(buffer, ref startIndex, sHA256);
      
      if (typeToken == TypesToken.AnchorToken)
        return new TXBTokenAnchor(buffer, ref startIndex, sHA256);
      
      if (typeToken == TypesToken.Data)
        return new TXBTokenData(buffer, ref startIndex, sHA256);

      throw new ProtocolException($"Unknown / wrong token type {typeToken}.");
    }

    protected override void InsertBlockInDatabase(Block block)
    {
      try
      {
        foreach (TXBToken tX in block.TXs)
        {
          foreach (TXOutputBToken tXOutput in tX.TXOutputs)
            StageOutput(tXOutput, block.Header.Height);

          StageSpendInput(tX);
        }

        foreach (var account in AccountsStaged)
          Cache[account.Key] = account.Value;
      }
      finally
      {
        AccountsStaged.Clear();
      }

      if (Cache.Count > COUNT_MAX_ACCOUNTS_IN_CACHE)
        EvictBlockFromCache();
    }

    void EvictBlockFromCache()
    {
      int heightBlock = DatabaseMetaCollection.FindById("lastProcessedBlock")["height"].AsInt32 + 1;

      while (Cache.Count > COUNT_MAX_ACCOUNTS_IN_CACHE * HYSTERESIS_COUNT_MAX_CACHE_ARCHIV)
        if (Network.TryLoadBlock(heightBlock, out Block block))
        {
          $"Loaded block {block} for insertion in disk database and removal from cache.".Log(this, LogEntryNotifier);

          RemoveAccountsFromCache(block);

          foreach (TXBToken tX in block.TXs)
          {
            if (!AccountsStaged.TryGetValue(tX.IDAccountSource, out Account accountSource))
            {
              accountSource = DatabaseAccountCollection.FindById(tX.IDAccountSource) ??
                throw new ProtocolException($"Account {tX.IDAccountSource.ToHexString()} referenced by TX {tX} not found in database.");

              if (accountSource.BlockHeightLastUpdated < heightBlock)
              {
                accountSource.BlockHeightLastUpdated = heightBlock;
                AccountsStaged.Add(accountSource.ID, accountSource);
              }
            }

            accountSource.Nonce += 1;
            accountSource.Balance -= tX.Fee + tX.GetValueOutputs();

            foreach (TXOutputBToken tXOutput in tX.TXOutputs)
            {
              if (!AccountsStaged.TryGetValue(tXOutput.IDAccount, out Account accountOutput))
              {
                accountOutput = DatabaseAccountCollection.FindById(tXOutput.IDAccount) ??
                  new()
                  {
                    ID = tXOutput.IDAccount,
                    BlockHeightAccountCreated = heightBlock
                  };

                if (accountOutput.BlockHeightLastUpdated < heightBlock)
                {
                  accountOutput.BlockHeightLastUpdated = heightBlock;
                  AccountsStaged.Add(accountOutput.ID, accountOutput);
                }
              }

              accountOutput.Balance += tXOutput.Value;
            }
          }

          List<byte[]> accountIDsWhereBalanceZero = AccountsStaged.Values.Where(a => a.Balance == 0).Select(a => a.ID).ToList();

          foreach (byte[] id in accountIDsWhereBalanceZero)
          {
            DatabaseAccountCollection.Delete(id);
            AccountsStaged.Remove(id);
          }

          foreach (var batch in AccountsStaged.Values.Chunk(500))
            DatabaseAccountCollection.Upsert(batch);

          DatabaseMetaCollection.Upsert(new BsonDocument
          {
            ["_id"] = "lastProcessedBlock",
            ["hash"] = block.Header.Hash,
            ["height"] = heightBlock
          });

          heightBlock += 1;
        }
        else
        {
          $"Failed to load block {block} for insertion in disk database and removal from cache.".Log(this, LogEntryNotifier);
          // Reload state
        }

      Database.Checkpoint();
    }

    Account GetCopyOfAccount(byte[] accountID)
    {
      if (Cache.TryGetValue(accountID, out Account accountCached))
        return new(accountCached);
      else if (DatabaseAccountCollection.FindById(accountID) is Account accountStored)
        return new(accountStored);
      else
        throw new ProtocolException($"Account {accountID.ToHexString()} not found in database.");
    }

    public Account GetCopyOfAccountUnconfirmed(byte[] iDAccount)
    {
      if (!TryLock())
        throw new SynchronizationLockException("Failed to acquire database lock.");

      try
      {
        return ((PoolTXBToken)TXPool).GetCopyOfAccount(iDAccount);
      }
      finally
      {
        ReleaseLock();
      }
    }

    void StageSpendInput(TXBToken tX)
    {
      if (tX is TXBTokenCoinbase)
        return;
      
        Account accountStaged = GetCopyOfAccount(tX.IDAccountSource);
        AccountsStaged.Add(accountStaged.ID, accountStaged);

      accountStaged.SpendTX(tX);
    }

    void StageOutput(TXOutputBToken output, int blockHeight)
    {
      if (output.Value <= 0)
        throw new ProtocolException($"Value of TX output funding {output.IDAccount.ToHexString()} is not greater than zero.");

      if (AccountsStaged.TryGetValue(output.IDAccount, out Account accountStaged))
        accountStaged.Balance += output.Value;
      else
      {
        if (Cache.TryGetValue(output.IDAccount, out Account accountCached))
          accountStaged = new()
          {
            ID = accountCached.ID,
            BlockHeightAccountCreated = accountCached.BlockHeightAccountCreated,
            Nonce = accountCached.Nonce,
            Balance = accountCached.Balance + output.Value
          };
        else if (DatabaseAccountCollection.FindById(output.IDAccount) is Account accountStored)
          accountStaged = new()
          {
            ID = accountStored.ID,
            BlockHeightAccountCreated = accountStored.BlockHeightAccountCreated,
            Nonce = accountStored.Nonce,
            Balance = accountStored.Balance + output.Value
          };
        else
          accountStaged = new()
          {
            ID = output.IDAccount,
            BlockHeightAccountCreated = blockHeight,
            Nonce = 0,
            Balance = output.Value
          };

        AccountsStaged.Add(output.IDAccount, accountStaged);
      }
    }
     
    void RemoveAccountsFromCache(Block block)
    {
      int heightBlock = block.Header.Height;

      foreach (TXBToken tX in block.TXs)
      {
        TryRemove(tX.IDAccountSource);

        foreach (TXOutputBToken outputBToken in tX.TXOutputs)
          TryRemove(outputBToken.IDAccount);
      }

      void TryRemove(byte[] id)
      {
        if (Cache.TryGetValue(id, out Account account) && account.BlockHeightLastUpdated == heightBlock)
          Cache.Remove(id);
      }
    }

    protected override void ReverseBlockInCache(Block block)
    {
      try
      {
        for (int i = block.TXs.Count - 1; i >= 0; i--)
        {
          TXBToken tX = block.TXs[i] as TXBToken;

          if (tX is TXBTokenCoinbase tXCoinbase)
          {
            foreach (TXOutputBToken tXOutput in tXCoinbase.TXOutputs)
              ReverseOutputInCache(tXOutput);
          }
          else
          {
            ReverseSpendInputInCache(tX);

            if (tX is TXBTokenValueTransfer tXTokenTransfer)
            {
              foreach (TXOutputBToken tXOutput in tXTokenTransfer.TXOutputs)
                ReverseOutputInCache(tXOutput);
            }
            else if (tX is TXBTokenAnchor tXBTokenAnchor)
            { }
            else if (tX is TXBTokenData tXBTokenData)
            { }
            else throw new ProtocolException($"Type of transaction {tX} is not supported by protocol.");
          }

          foreach (var account in AccountsStaged)
            Cache[account.Key] = account.Value;
        }
      }
      finally
      {
        AccountsStaged.Clear();
      }
    }

    void ReverseOutputInCache(TXOutputBToken output)
    {
      if (!AccountsStaged.TryGetValue(output.IDAccount, out Account accountStaged))
      {
        if (!Cache.TryGetValue(output.IDAccount, out accountStaged))
          throw new ProtocolException($"TX Output cannot be reversed because account {output.IDAccount.ToHexString()} does not exist in cache.");

        AccountsStaged.Add(output.IDAccount, accountStaged);
      }

      accountStaged.Balance -= output.Value;
    }

    public void ReverseSpendInputInCache(TXBToken tX)
    {
      if (!AccountsStaged.TryGetValue(tX.IDAccountSource, out Account accountStaged))
      {
        if (!Cache.TryGetValue(tX.IDAccountSource, out accountStaged))
          accountStaged = new()
          {
            ID = tX.IDAccountSource,
            BlockHeightAccountCreated = tX.BlockheightAccountCreated,
            Nonce = tX.Nonce,
          };

        AccountsStaged.Add(tX.IDAccountSource, accountStaged);
      }

      accountStaged.ReverseSpendTX(tX);
    }

    public override List<byte[]> ParseHashesDB(byte[] buffer, int length, Header headerTip)
    {
      SHA256 sHA256 = SHA256.Create();

      byte[] hashRootHashesDB = sHA256.ComputeHash(buffer, 0, length);

      if (!((HeaderBToken)headerTip).HashDatabase.IsAllBytesEqual(hashRootHashesDB))
        throw new ProtocolException($"Root hash of hashesDB not equal to database hash in header tip");

      List<byte[]> hashesDB = new();

      //for (int i = 0; i < DBAccounts.COUNT_CACHES + DBAccounts.COUNT_FILES_DB; i += 32)
      //{
      //  byte[] hashDB = new byte[32];
      //  Array.Copy(buffer, i, hashDB, 0, 32);
      //  hashesDB.Add(hashDB);
      //}

      return hashesDB;
    }
      
    byte[] GetGenesisBlockBytes()
    {
      return new byte[285]{
        0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x3b, 0xa3, 0xed, 0xfd, 0x7a, 0x7b, 0x12, 0xb2, 0x7a, 0xc7, 0x2c, 0x3e,
        0x67, 0x76, 0x8f, 0x61, 0x7f, 0xc8, 0x1b, 0xc3, 0x88, 0x8a, 0x51, 0x32, 0x3a, 0x9f, 0xb8, 0xaa,
        0x4b, 0x1e, 0x5e, 0x4a, 0x29, 0xab, 0x5f, 0x49, 0xff, 0xff, 0x00, 0x1d, 0x1d, 0xac, 0x2b, 0x7c,
        0x01, 0x01, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xff, 0xff, 0xff, 0xff, 0x4d, 0x04, 0xff, 0xff, 0x00, 0x1d,
        0x01, 0x04, 0x45, 0x54, 0x68, 0x65, 0x20, 0x54, 0x69, 0x6d, 0x65, 0x73, 0x20, 0x30, 0x33, 0x2f,
        0x4a, 0x61, 0x6e, 0x2f, 0x32, 0x30, 0x30, 0x39, 0x20, 0x43, 0x68, 0x61, 0x6e, 0x63, 0x65, 0x6c,
        0x6c, 0x6f, 0x72, 0x20, 0x6f, 0x6e, 0x20, 0x62, 0x72, 0x69, 0x6e, 0x6b, 0x20, 0x6f, 0x66, 0x20,
        0x73, 0x65, 0x63, 0x6f, 0x6e, 0x64, 0x20, 0x62, 0x61, 0x69, 0x6c, 0x6f, 0x75, 0x74, 0x20, 0x66,
        0x6f, 0x72, 0x20, 0x62, 0x61, 0x6e, 0x6b, 0x73, 0xff, 0xff, 0xff, 0xff, 0x01, 0x00, 0xf2, 0x05,
        0x2a, 0x01, 0x00, 0x00, 0x00, 0x43, 0x41, 0x04, 0x67, 0x8a, 0xfd, 0xb0, 0xfe, 0x55, 0x48, 0x27,
        0x19, 0x67, 0xf1, 0xa6, 0x71, 0x30, 0xb7, 0x10, 0x5c, 0xd6, 0xa8, 0x28, 0xe0, 0x39, 0x09, 0xa6,
        0x79, 0x62, 0xe0, 0xea, 0x1f, 0x61, 0xde, 0xb6, 0x49, 0xf6, 0xbc, 0x3f, 0x4c, 0xef, 0x38, 0xc4,
        0xf3, 0x55, 0x04, 0xe5, 0x1e, 0xc1 ,0x12, 0xde, 0x5c, 0x38, 0x4d, 0xf7, 0xba, 0x0b, 0x8d, 0x57,
        0x8a, 0x4c, 0x70, 0x2b, 0x6b, 0xf1, 0x1d, 0x5f, 0xac, 0x00, 0x00 ,0x00 ,0x00 };
    }

    public override void Reset()
    {
      base.Reset();
      Cache.Clear();
    }

    public override List<string> GetSeedAddresses()
    {
      return new List<string>()
      {
        "83.229.86.158" 
        //84.74.69.100
      };
    }

    public override HeaderBToken ParseHeader(byte[] buffer, ref int index, SHA256 sHA256)
    {
      byte[] hash =
        sHA256.ComputeHash(
          sHA256.ComputeHash(
            buffer,
            index,
            HeaderBToken.COUNT_HEADER_BYTES));

      byte[] hashHeaderPrevious = new byte[32];
      Array.Copy(buffer, index, hashHeaderPrevious, 0, 32);
      index += 32;

      byte[] merkleRootHash = new byte[32];
      Array.Copy(buffer, index, merkleRootHash, 0, 32);
      index += 32;

      byte[] hashDatabase = new byte[32];
      Array.Copy(buffer, index, hashDatabase, 0, 32);
      index += 32;

      uint unixTimeSeconds = BitConverter.ToUInt32(
        buffer, index);
      index += 4;

      uint nonce = BitConverter.ToUInt32(buffer, index);
      index += 4;

      return new HeaderBToken(
        hash,
        hashHeaderPrevious,
        merkleRootHash,
        hashDatabase,
        unixTimeSeconds,
        nonce);
    }
  }
}
