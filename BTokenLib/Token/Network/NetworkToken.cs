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

      // Jetzt glaub ich wieder, dass die Wallet doch ins token muss, 
      // weil die gemeinsamkeit besteht, dass sie immer über Blockchain 
      // operation informiert werden müssen.
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


      public NetworkToken(
        Token tokenParent,
        Token token,
        int port,
        bool flagEnableInboundConnections,
        bool flagEnableRelay)
      {
        NetworkParent = tokenParent.Network; 
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
          if (BlockchainRoot.TryInsertBlock(ref block, ref BlockchainRoot, out isSyncComplete))
            NotifyChildTokensOfAnchorToken(block);
        }
        finally
        {
          ReleaseLockBlockchain();
        }

        if (isSyncComplete)
          foreach (NetworkToken networkChild in NetworksChild)
            networkChild.StartHeaderSync();
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
            string pathFileBlock = Path.Combine(PathBlocksMined, blockMined.Header.Height.ToString());

            if (!File.Exists(pathFileBlock))
              return;

            blockMined = new(Token, File.ReadAllBytes(pathFileBlock));
            blockMined.Parse();
          }

          if (BlockchainRoot.TryExtendHeaderchain(blockMined.Header, out List<byte[]> headerslocator, blockMined))
            if (BlockchainRoot.TryInsertBlock(ref blockMined, ref BlockchainRoot, out bool isSyncComplete))
            {
              // Hier ein sendBlock machen und intern zuerst header und dann wenn
              // getdata kommt blcok aus peer cache laden, statt wieder node anfragen.
              lock (LOCK_Peers)
                Peers.ForEach(p => HeadersMessage.SendHeaders(
                  p,
                  new List<byte[]> { blockMined.Header.Hash }));

              NotifyChildTokensOfAnchorToken(blockMined);
            }

          // Der User muss jeweils definieren, mit welcher fee Rate er die Verankerung bezahlen will.
          // Dem user kann im GUI auch ein Tool zur verfügung gestellt werden welches ihm 
          // erlaubt, die Fee Rate automatisiert zu steuern. z.B. anhand vergangener Fee Raten
          // oder Marktpreis Arbitrierung.

          if (IsMining)
          {
            Block block = BlockchainRoot.MineBlock(out TXOutputTokenAnchor anchorToken);

            BlocksMinedCache.Add(block);

            block.WriteToDisk(PathBlocksMined);

            NetworkParent.MineTokenAnchor(tokenAnchor);
          }
        }
        catch (Exception ex)
        {
          $"{ex.GetType().Name} when attempting to load mined block {tokenAnchor.HashBlockReferenced.ToHexString()}: {ex.Message}.\n".Log(this, LogEntryNotifier);
        }
      }

      void MineTokenAnchor(TXOutputTokenAnchor tokenAnchor)
      {
        if (Token.TryCreateTXAnchor(tokenAnchor, out TX tXAnchor))
          lock (LOCK_Peers)
            foreach (Peer peer in Peers)
              peer.BroadcastTX(tXAnchor);
        else
        {
          $"Could not create anchor tX, stop mining.".Log(this, LogEntryNotifier);
          IsMining = false;
        }
      }

      void NotifyChildTokensOfAnchorToken(Block block)
      {
        Dictionary<byte[], TXOutputTokenAnchor> cacheAnchorTokens =
          new(new EqualityComparerByteArray());

        foreach (TX tX in block.TXs)
          foreach (TXOutput tXOutput in tX.TXOutputs)
            if (tXOutput is TXOutputTokenAnchor tokenAnchor)
              if (cacheAnchorTokens.TryAdd(tokenAnchor.HashBlockReferenced, tokenAnchor))
                NetworksChild.Find(n => n.Token.IDToken.IsAllBytesEqual(tokenAnchor.IDToken))
                  ?.OnTokenAnchorParent(tokenAnchor);
      }

      const int COUNT_BYTES_PER_BLOCK_MAX = 1000;
      const int TIMESPAN_MINING_ANCHOR_TOKENS_SECONDS = 4;
      const int TIME_MINER_PAUSE_AFTER_RECEIVE_PARENT_BLOCK_SECONDS = 5;
      const double FACTOR_INCREMENT_FEE_PER_BYTE_ANCHOR_TOKEN = 1.02;
      const double MINIMUM_FEE_SATOSHI_PER_BYTE_ANCHOR_TOKEN = 0.1;


      string PathBlocksMined = "blocksMined";


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