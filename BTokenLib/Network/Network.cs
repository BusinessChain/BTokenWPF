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
    protected Token Token;
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
      Log($"Start Network.");

      StartPeerConnector();

      LoadBlocksFromArchive();
    }

    void LoadBlocksFromArchive()
    {
      $"Load Blocks from disk.".Log(this, LogEntryNotifier);

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

    public bool TryLoadBlock(byte[] hash, out Block block)
    {
      block = null;

      if (TryLoadHeader(hash, out Header header))
        return TryLoadBlock(header.Height, out block);

      return false;
    }

    bool TryLoadHeader(byte[] hash, out Header header)
    {
      header = HeaderTip;

      while (header != null && !header.Hash.IsAllBytesEqual(hash))
        header = header.HeaderPrevious;

      return header != null;
    }

    public bool TryLoadBlock(int blockHeight, out Block block)
    {
      block = null;
      string pathBlock = Path.Combine(PathBlockArchive, blockHeight.ToString());

      while (true)
        try
        {
          block = new(Token, File.ReadAllBytes(pathBlock));
          block.Parse(blockHeight);

          return true;
        }
        catch (FileNotFoundException)
        {
          return false;
        }
        catch (IOException ex)
        {
          ($"{ex.GetType().Name} when attempting to load file {pathBlock}: {ex.Message}.\n" +
            $"Retry in {TIMEOUT_FILE_RELOAD_SECONDS} seconds.").Log(this, Token.LogEntryNotifier);

          Thread.Sleep(TIMEOUT_FILE_RELOAD_SECONDS * 1000);
        }
        catch (Exception ex)
        {
          $"{ex.GetType().Name} when loading block height {blockHeight} from disk. Block deleted."
          .Log(this, Token.LogEntryNotifier);

          File.Delete(Path.Combine(PathBlockArchive, blockHeight.ToString()));

          return false;
        }
    }

    public void AdvertizeBlockToNetwork(Block block)
    {
      lock (LOCK_Peers)
        Peers.ForEach(p => p.AdvertizeBlock(block));
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
