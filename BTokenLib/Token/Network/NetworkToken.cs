using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

using LiteDB;


namespace BTokenLib
{
  public abstract partial class Token
  {
    protected partial class NetworkToken
    {
      public NetworkToken NetworkParent;

      public List<NetworkToken> NetworksChild = new();

      public Token Token;

      public bool EnableInboundConnections;
      public bool EnableRelay;
      public ILogEntryNotifier LogEntryNotifier;

      object LOCK_Peers = new();
      List<Peer> Peers = new();

      DirectoryInfo DirectoryPeers;
      DirectoryInfo DirectoryPeersActive;
      DirectoryInfo DirectoryPeersArchive;
      DirectoryInfo DirectoryPeersDisposed;

      Blockchain BlockchainRoot;

      SemaphoreSlim SemaphoreBlockchainRoot = new(1);

      readonly object LOCK_FlagSyncLocked = new object();
      bool FlagSyncLocked;

      LiteDatabase LiteDatabase;
      ILiteCollection<BsonDocument> DatabaseMetaCollection;
      ILiteCollection<BsonDocument> DatabaseHeaderCollection;

      Dictionary<byte[], TXOutputTokenAnchor> CacheAnchorTokens =
        new(new EqualityComparerByteArray());


      public NetworkToken(
        Token tokenParent,
        Token token,
        int port,
        bool flagEnableInboundConnections,
        bool flagEnableRelay)
        : this(token, port, flagEnableInboundConnections, flagEnableRelay)
      {
        NetworkParent = tokenParent.Network;
      }

      public NetworkToken(
        Token token,
        int port,
        bool flagEnableInboundConnections,
        bool flagEnableRelay)
      {
        Token = token;

        BlockchainRoot = new(Token);

        EnableInboundConnections = flagEnableInboundConnections;
        EnableRelay = flagEnableRelay;

        LogEntryNotifier = token.LogEntryNotifier;
        string pathRoot = token.GetName();

        DirectoryPeers = Directory.CreateDirectory(
          Path.Combine(pathRoot, "logPeers"));

        DirectoryPeersActive = Directory.CreateDirectory(
          Path.Combine(DirectoryPeers.FullName, "active"));

        DirectoryPeersDisposed = Directory.CreateDirectory(
          Path.Combine(DirectoryPeers.FullName, "disposed"));

        DirectoryPeersArchive = Directory.CreateDirectory(
          Path.Combine(DirectoryPeers.FullName, "archive"));

        foreach (FileInfo file in DirectoryPeersActive.GetFiles())
          file.MoveTo(Path.Combine(DirectoryPeersArchive.FullName, file.Name));

        LiteDatabase = new LiteDatabase($"{token.GetName()}.db;Mode=Exclusive");
        DatabaseHeaderCollection = LiteDatabase.GetCollection<BsonDocument>("headers");
        DatabaseMetaCollection = LiteDatabase.GetCollection<BsonDocument>("meta");
      }

      public void Start()
      {
        if (NetworkParent != null)
          NetworkParent.Start();

        Log($"Load state from disk.");
        BlockchainRoot.LoadFromDisk();

        Log($"Start Network.");
        StartPeerConnector();
      }

      public void StartHeaderSync()
      {
        Peers.ForEach(p => StartHeaderSync(p));
      }

      async Task StartHeaderSync(Peer peer)
      {
        if (!await TryLockBlockchain(10000))
          return;

        try
        {
          if (NetworkParent.BlockchainRoot.GetHeight() > BlockchainRoot.GetHeight())
            GetHeadersMessage.SendGetHeaders(peer, GetLocator());
        }
        finally
        {
          ReleaseLockBlockchain();
        }
      }

      async Task<bool> TryLockBlockchain(int timeout)
      {
        if (NetworkParent != null)
          return await NetworkParent.TryLockBlockchain(timeout);

        return await SemaphoreBlockchainRoot.WaitAsync(timeout).ConfigureAwait(false);
      }

      void ReleaseLockBlockchain()
      {
        if (NetworkParent != null)
          NetworkParent.ReleaseLockBlockchain();
        else
          SemaphoreBlockchainRoot.Release();
      }

      public async Task<List<byte[]>> ExtendHeaderchain(
        Header headerRoot,
        Block blockDownload)
      {
        List<byte[]> headerslocator = null;

        if (!await TryLockBlockchain(10000))
          return headerslocator;

        try
        {
          BlockchainRoot.TryExtendHeaderchain(
            headerRoot,
            out headerslocator,
            blockDownload);

          return headerslocator;
        }
        finally
        {
          ReleaseLockBlockchain();
        }
      }

      async Task InsertBlock(Block block)
      {
        if (!await TryLockBlockchain(10000))
          return;

        bool isSyncComplete = false;

        try
        {
          BlockchainRoot.TryInsertBlock(
            ref block,
            ref BlockchainRoot,
            out isSyncComplete);

          DetectTokensAnchor(block);
        }
        finally
        {
          ReleaseLockBlockchain();
        }

        if (isSyncComplete)
          foreach (NetworkToken networkChild in NetworksChild)
            networkChild.StartHeaderSync();
      }

      void DetectTokensAnchor(Block block)
      {
        foreach (TX tX in block.TXs)
          foreach (TXOutput tXOutput in tX.TXOutputs)
            if (tXOutput is TXOutputTokenAnchor tokenAnchor)
            {
              if (CacheAnchorTokens.Any(t => t.Value.IDToken.IsAllBytesEqual(tokenAnchor.IDToken)))
                continue;

              CacheAnchorTokens.Add(
                tokenAnchor.HashBlockReferenced,
                tokenAnchor);

              NetworkToken networkChild = NetworksChild
                .Find(n => n.Token.IDToken.IsAllBytesEqual(tokenAnchor.IDToken));

              if (networkChild != null)
                networkChild.OnTokenAnchorParent(tokenAnchor);
            }
      }

      List<Block> BlocksMinedCache = new();

      void OnTokenAnchorParent(TXOutputTokenAnchor tokenAnchor)
      {
        try
        {
          Block blockMined = BlocksMinedCache
            .Find(b => b.Header.Hash.IsAllBytesEqual(tokenAnchor.HashBlockReferenced));

          if (blockMined == null)
          {
            blockMined = new(Token, File.ReadAllBytes(Path.Combine(PathBlocksMined, blockMined.Header.Height.ToString())));
            blockMined.Parse();
          }

          InsertBlock(blockMined);

          Peers.ForEach(p => HeadersMessage.SendHeaders(
            p, 
            new List<byte[]> { blockMined.Header.Hash }));

          if(IsMining)
          {
            int height = BlockchainRoot.GetHeight() + 1;

            Block block = Token.CreateBlock(
              BlockchainRoot,
              height, 
              out long feeTXs, 
              out byte[] dataAnchorToken);

            NetworkParent.BroadcastAnchorToken(dataAnchorToken);

            BlocksMinedCache.Add(block);

            block.WriteToDisk(PathBlocksMined);
          }
        }
        catch (Exception ex)
        {
          $"{ex.GetType().Name} when attempting to load mined block {tokenAnchor.HashBlockReferenced.ToHexString()}: {ex.Message}.\n".Log(this, LogEntryNotifier);
        }
      }

      const int COUNT_BYTES_PER_BLOCK_MAX = 1000;
      const int TIMESPAN_MINING_ANCHOR_TOKENS_SECONDS = 4;
      const int TIME_MINER_PAUSE_AFTER_RECEIVE_PARENT_BLOCK_SECONDS = 5;
      const double FACTOR_INCREMENT_FEE_PER_BYTE_ANCHOR_TOKEN = 1.02;
      const double MINIMUM_FEE_SATOSHI_PER_BYTE_ANCHOR_TOKEN = 0.1;


      void BroadcastAnchorToken(byte[] dataAnchorToken)
      {
        // Die Wallet nur für Signatur verwenden.
        Wallet.SendTXData(dataAnchorToken, FeeSatoshiPerByteAnchorToken);
      }

      string PathBlocksMined;


      List<byte[]> GetLocator()
      {
        lock (BlockchainRoot)
          return BlockchainRoot.GetLocator();
      }

      async Task<(List<byte[]> headers, int heightAncestor)> GetHeadersSerialized(
        List<byte[]> hashesLocator,
        int maxCountHeaders)
      {
        if (!await TryLockBlockchain(10000))
          return (headers: new(), heightAncestor: -1);

        try
        {
          return BlockchainRoot.GetHeadersSerialized(hashesLocator, maxCountHeaders);
        }
        finally
        {
          ReleaseLockBlockchain();
        }
      }

      public async Task GetBlock(byte[] hash, Block blockUpload)
      {
        if (!await TryLockBlockchain(10000))
          return;

        try
        {
          BlockchainRoot.GetBlock(hash, blockUpload);
        }
        finally
        {
          ReleaseLockBlockchain();
        }
      }

      public void BroadcastTX(Token.TX tX)
      {
        $"Advertize token {tX}.".Log(this, Token.LogEntryNotifier);

        lock (LOCK_Peers)
          foreach (Peer peer in Peers)
            peer.BroadcastTX(tX);
      }

      public double GetFeeRate()
      {
        // return average block fee devided by block bytes
        // return average fee/byte.
        return 0;
      }

      bool IsMining;

      public void StartMining()
      {
        if (NetworkParent == null)
          return;

        IsMining = true;
      }

      public string GetStatus()
      {
        return "Todo";
        //string messageStatus = "";

        //messageStatus +=
        //  $"Height: {HeaderTip.Height}\n" +
        //  $"Block tip: {HeaderTip.Hash.ToHexString().Substring(0, 24) + " ..."}\n" +
        //  $"Difficulty Tip: {HeaderTip.Difficulty}\n" +
        //  $"Acc. Difficulty: {HeaderTip.DifficultyAccumulated}\n";

        //string statusPeers = "";
        //int countPeers;

        //lock (LOCK_Peers)
        //{
        //  Peers.ForEach(p => { statusPeers += p.GetStatus(); });
        //  countPeers = Peers.Count;
        //}

        //return
        //  $"\n Status Network: \n " +
        //  $"{messageStatus} \n " +
        //  $"{statusPeers} \n " +
        //  $"Count peers: {countPeers}";
      }

      readonly object Lock_StateNetwork = new object();

      public void Log(string messageLog)
      {
        messageLog.Log(this, LogEntryNotifier);
      }

      public override string ToString()
      {
        return Token.GetType().Name + "." + GetType().Name;
      }
    }
  }
}