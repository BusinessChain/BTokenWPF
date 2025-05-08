using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace BTokenLib
{
  public partial class Network
  {
    Token Token;

    UInt16 Port;

    public bool EnableInboundConnections;

    object LOCK_Peers = new();
    List<Peer> Peers = new();

    DirectoryInfo DirectoryPeers;
    DirectoryInfo DirectoryPeersActive;
    DirectoryInfo DirectoryPeersArchive;
    DirectoryInfo DirectoryPeersDisposed;


    public Network(
      Token token,
      UInt16 port,
      bool flagEnableInboundConnections)
    {
      Token = token;

      Port = port;
      EnableInboundConnections = flagEnableInboundConnections;

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

    public void Start()
    {
      $"Start Network {Token.GetName()}".Log(this, Token.LogFile, Token.LogEntryNotifier);

      StartPeerConnector();

      if (EnableInboundConnections)
        StartPeerInboundConnector();
    }

    void LoadNetworkConfiguration(string pathConfigFile)
    {
      $"Load Network configuration {pathConfigFile}."
        .Log(this, Token.LogFile, Token.LogEntryNotifier);
    }

    public void AdvertizeBlockToNetwork(Block block)
    {
      lock (LOCK_Peers)
        Peers.ForEach(p => p.AdvertizeBlock(block));
    }

    bool TryGetPeerIdle(out Peer peer, Peer.StateProtocol stateNew)
    {
      lock (LOCK_Peers)
      {
        peer = Peers.Find(p => p.IsStateIdle()); // This is not yet done correctly,
                                                 // if lock on peer level are needed,
                                                 // then the state transitions should be within IsStateIdle
                                                 // But maybe it is better if we have no individual peer-level lock
        peer.State = stateNew;
      }

      return peer != null;
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
