using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

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


    public Network(
      Token token,
      byte[] iDToken, 
      UInt16 port, 
      bool flagEnableInboundConnections)
    {
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

      LoadNetworkConfiguration(pathRoot);

      foreach (FileInfo file in DirectoryPeersActive.GetFiles())
        file.MoveTo(Path.Combine(DirectoryPeersArchive.FullName, file.Name));
    }

    public async Task Start()
    {
      $"Start Network {Token.GetName()}".Log(this, Token.LogFile, LogEntryNotifier);

      StartPeerConnector();

      if (EnableInboundConnections)
        StartPeerInboundConnector();

      while (!FlagInitialSyncSucceed)
        await Task.Delay(1000).ConfigureAwait(false);
    }

    void LoadNetworkConfiguration(string pathConfigFile)
    {
      $"Load Network configuration {pathConfigFile}."
        .Log(this, Token.LogFile, LogEntryNotifier);
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
      string statusPeers = "";
      int countPeers;

      lock (LOCK_Peers)
      {
        Peers.ForEach(p => { statusPeers += p.GetStatus(); });
        countPeers = Peers.Count;
      }

      return
        "\n Status Network: \n" +
        statusPeers +
        $"Count peers: {countPeers} \n";
    }

    public override string ToString()
    {
      return Token.GetType().Name + "." + GetType().Name;
    }
  }
}
