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

      // Bekommt ein Netzwerk ein Block
      // werden die Ankertoken in den ChildNetzwerken vermerkt. Wenn dann die Childnetzwerke gesyncted
      // werden, soll keine Abhängigkeit zum Parent mehr bestehen, so dass die ChildNetzwerke parallel
      // gesyncet werden können. 
      // False isMining gleich true, dann wird auch noch gerade geschaut,
      // ob ein Block mined wurde zu broadcasten.
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

      SemaphoreSlim SemaphoreBlockchainRootRoot = new(1);

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

        return await SemaphoreBlockchainRootRoot.WaitAsync(timeout).ConfigureAwait(false);
      }

      void ReleaseLockBlockchain()
      {
        if (NetworkParent != null)
          NetworkParent.ReleaseLockBlockchain();
        else
          SemaphoreBlockchainRootRoot.Release();
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

      async Task InsertBlock(Peer peer, Block block)
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
        }
        finally
        {
          ReleaseLockBlockchain();
        }

        if (isSyncComplete)
          foreach (NetworkToken networkChild in NetworksChild)
            networkChild.StartHeaderSync();
        //irgendwo ab hier muss der miner getriggert werden.
        //und selbst geminte Blöcke eingefügt/broadcastet werden.
      }

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

        if (IsMining)
        {
          IsMining = false;
          return;
        }

        IsMining = true;

        // Wenn ein Child Netzwerk notified wird, dass im Parent ein Block eingefügt wurde,
        // wird im Falle von IsMining == true geprüft, ob anker Token drin erstes ist
        // und wenn Ja Block posten und neues AT machen (und Block) machen.
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