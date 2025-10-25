using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Security.Cryptography;

using LiteDB;


namespace BTokenLib
{
  public partial class Network
  {
    protected Token Token;
    public byte[] IDToken;
    protected UInt16 Port;
    public bool EnableInboundConnections;
    public ILogEntryNotifier LogEntryNotifier;

    object LOCK_Peers = new();
    List<Peer> Peers = new();

    DirectoryInfo DirectoryPeers;
    DirectoryInfo DirectoryPeersActive;
    DirectoryInfo DirectoryPeersArchive;
    DirectoryInfo DirectoryPeersDisposed;


    public Header HeaderTip;
    public Header HeaderGenesis;

    protected LiteDatabase LiteDatabase;
    protected ILiteCollection<BsonDocument> DatabaseMetaCollection;
    protected ILiteCollection<BsonDocument> DatabaseHeaderCollection;

    public Network(
      Token token,
      byte[] iDToken, 
      UInt16 port, 
      bool flagEnableInboundConnections)
    {
      Token = token;
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
      
      HeaderGenesis = Token.CreateHeaderGenesis();
    }

    public async Task Start()
    {
      $"Load Blocks from disk.".Log(this, Token.LogFile, LogEntryNotifier);

      LoadBlocksFromArchive();

      $"Start Network.".Log(this, Token.LogFile, LogEntryNotifier);

      StartPeerConnector();

      if (EnableInboundConnections)
        StartPeerInboundConnector();

      while (!FlagInitialSyncSucceed)
        await Task.Delay(1000).ConfigureAwait(false);
    }

    void LoadBlocksFromArchive()
    {
      int heightBlockNext = Directory.GetFiles(PathBlockArchive, "*.blk")
      .Select(Path.GetFileNameWithoutExtension)
      .Where(name => int.TryParse(name, out _))
      .Select(name => int.Parse(name))
      .DefaultIfEmpty(0)               
      .Min();

      while (TryLoadBlock(heightBlockNext, out Block block))
        try
        {
          if (HeaderTip == null)
            HeaderTip = block.Header;
          else
            block.Header.AppendToHeader(HeaderTip);

          Token.InsertBlock(block);

          HeaderTip.HeaderNext = block.Header;
          HeaderTip = block.Header;

          heightBlockNext += 1;
        }
        catch (ProtocolException ex)
        {
          $"{ex.GetType().Name} when inserting block {block}, height {heightBlockNext} loaded from disk: \n{ex.Message}. \nBlock is deleted."
          .Log(this, LogEntryNotifier);

          File.Delete(Path.Combine(PathBlockArchive, heightBlockNext.ToString()));
        }

      HeaderTip ??= HeaderGenesis;
    }

    public void AdvertizeBlockToNetwork(Block block)
    {
      lock (LOCK_Peers)
        Peers.ForEach(p => p.AdvertizeBlock(block));
    }

    public void AdvertizeTX(TX tX)
    {
      lock (LOCK_Peers)
        foreach (Peer peer in Peers)
          peer.TryAdvertizeTX(tX);
    }

    public void AdvertizeTXs(List<TX> tXs)
    {
      lock (LOCK_Peers)
        foreach (Peer peer in Peers)
          peer.TryAdvertizeTXs(tXs);
    }

    public List<Peer> GetPeers()
    {
      lock (LOCK_Peers)
        return Peers.ToList();
    }

    public string GetStatus()
    {
      string messageStatus = "";

      var ageBlock = TimeSpan.FromSeconds(
        DateTimeOffset.UtcNow.ToUnixTimeSeconds() - HeaderTip.UnixTimeSeconds);

      messageStatus +=
        $"Height: {HeaderTip.Height}\n" +
        $"Block tip: {HeaderTip.Hash.ToHexString().Substring(0, 24) + " ..."}\n" +
        $"Difficulty Tip: {HeaderTip.Difficulty}\n" +
        $"Acc. Difficulty: {HeaderTip.DifficultyAccumulated}\n" +
        $"Timestamp: {DateTimeOffset.FromUnixTimeSeconds(HeaderTip.UnixTimeSeconds)}\n" +
        $"Age: {ageBlock}\n";

      string statusPeers = "";
      int countPeers;

      lock (LOCK_Peers)
      {
        Peers.ForEach(p => { statusPeers += p.GetStatus(); });
        countPeers = Peers.Count;
      }

      return
        $"\n Status Network: \n " +
        $"{messageStatus} \n " +
        $"{statusPeers} \n " +
        $"Count peers: {countPeers}";
    }

    public override string ToString()
    {
      return Token.GetType().Name + "." + GetType().Name;
    }
  }
}
