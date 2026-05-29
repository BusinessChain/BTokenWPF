using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

using LiteDB;


namespace BTokenLib
{
  public partial class Network
  {
    public Network NetworkParent;
    // Bekommt ein Netzwerk ein Block
    // werden die Ankertoken in den ChildNetzwerken vermerkt. Wenn dann die Childnetzwerke gesyncted
    // werden, soll keine Abhängigkeit zum Parent mehr bestehen, so dass die ChildNetzwerke parallel
    // gesyncet werden können.
    public List<Network> NetworksChild = new();

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
    public const int TIMEOUT_FILE_RELOAD_SECONDS = 10;

    Synchronization SynchronizationRoot;

    SemaphoreSlim SemaphoreSynchronizationRoot = new(1);

    readonly object LOCK_FlagSyncLocked = new object();
    bool FlagSyncLocked;

    LiteDatabase LiteDatabase;
    ILiteCollection<BsonDocument> DatabaseMetaCollection;
    ILiteCollection<BsonDocument> DatabaseHeaderCollection;

    public Network(
      Token token,
      int port, 
      bool flagEnableInboundConnections,
      bool flagEnableRelay)
    {
      Token = token;

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
      SynchronizationRoot.LoadFromDisk();

      Log($"Start Network.");
      StartPeerConnector();
    }

    async Task StartHeaderSync(Peer peer)
    {
      if (!await TryLockSynchronization(10000)) 
        return;

      try
      {
        if (SynchronizationRoot.HeaderTip.Height < NetworkParent.SynchronizationRoot.HeaderTip.Height)
          GetHeadersMessage.SendGetHeaders(peer, GetLocator());
      }
      finally
      {
        ReleaseLockSynchronization();
      }
    }

    async Task<bool> TryLockSynchronization(int timeout)
    {
      if (NetworkParent != null)
        return await NetworkParent.TryLockSynchronization(timeout);

      return await SemaphoreSynchronizationRoot.WaitAsync(timeout).ConfigureAwait(false);
    }

    void ReleaseLockSynchronization()
    {
      if (NetworkParent != null)
        NetworkParent.ReleaseLockSynchronization();
      else
        SemaphoreSynchronizationRoot.Release();
    }

    public async Task<List<byte[]>> ExtendHeaderchain(
      Header headerRoot,
      Block blockDownload)
    {
      List<byte[]> headerslocator = null;

      if (!await TryLockSynchronization(10000))
        return headerslocator;

      try
      {
        SynchronizationRoot.TryExtendHeaderchain(
          headerRoot,
          out headerslocator,
          blockDownload);

        return headerslocator;
      }
      finally
      {
        ReleaseLockSynchronization();
      }
    }

    async Task InsertBlock(Peer peer, Block block)
    {
      if (!await TryLockSynchronization(10000))
        return;

      try
      {
        SynchronizationRoot.TryInsertBlock(ref block, ref SynchronizationRoot);
      }
      finally
      {
        ReleaseLockSynchronization();
      }
    }

    List<byte[]> GetLocator()
    {
      lock (SynchronizationRoot)
        return SynchronizationRoot.GetLocator();
    }

    async Task<Header> LoadHeaderAncestor(List<byte[]> hashesLocator)
    {
      Header headerAncestor = null;

      if (!await TryLockSynchronization(10000))
        return headerAncestor;

      try
      {
        headerAncestor = SynchronizationRoot.HeaderTip;

        while (headerAncestor != null)
        {
          foreach (byte[] hashLocator in hashesLocator)
            if (headerAncestor.Hash.IsAllBytesEqual(hashLocator))
              return headerAncestor;

          headerAncestor = headerAncestor.HeaderPrevious;
        }

        return headerAncestor;
      }
      finally
      {
        ReleaseLockSynchronization();
      }
    }
    
    public async Task<Block> LoadBlock(byte[] hash)
    {
      Block block = null;

      if (!await TryLockSynchronization(10000))
        return block;

      try
      {
        block = SynchronizationRoot.GetBlock(hash);
        return block;
      }
      finally
      {
        ReleaseLockSynchronization();
      }
    }

    public async Task<Block> LoadBlock(int height)
    {
      Block block = null;

      if (!await TryLockSynchronization(10000))
        return block;

      try
      {
        block = SynchronizationRoot.GetBlock(height);
        return block;
      }
      finally
      {
        ReleaseLockSynchronization();
      }
    }

    public void BroadcastTX(TX tX)
    {
      $"Advertize token {tX}.".Log(this, Token.LogEntryNotifier);

      lock (LOCK_Peers)
        foreach (Peer peer in Peers)
          peer.BroadcastTX(tX);
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