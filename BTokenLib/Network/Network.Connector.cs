using System;
using System.IO;
using System.Net;
using System.Linq;
using System.Net.Sockets;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Collections.Generic;


namespace BTokenLib
{
  partial class Network
  {
    const int TIMESPAN_LOOP_PEER_CONNECTOR_SECONDS = 10;
    const int TIMESPAN_PEER_BANNED_SECONDS = 10;
    int CountMaxPeers = 3;

    const int COUNT_MAX_INBOUND_CONNECTIONS = 3;

    public bool FlagEnableOutboundConnections = true;

    public enum StateNetwork
    {
      Idle,
      ConnectingPeerOutbound,
      ConnectingPeerInbound
    }
    public StateNetwork State = StateNetwork.Idle;

    public enum ConnectionType { OUTBOUND, INBOUND };

    List<string> IPAddresses = new();

    async Task StartPeerConnector()
    {
      //if (EnableInboundConnections)
      //  StartPeerInboundConnector();

      Random randomGenerator = new();

      while (true)
      {
        try
        {
          Peers.RemoveAll(p => p.StateCurrent == Peer.StateProtocol.Disposed);

          int countPeersCreate = CountMaxPeers - Peers.Count;

          if (countPeersCreate > 0)
          {
            List<string> iPAddresses = LoadIPAddresses(countPeersCreate, randomGenerator);

            var createPeerTasks = iPAddresses
              .Select(ip => CreatePeer(ip))
              .ToArray();

            await Task.WhenAll(createPeerTasks);
          }

          int timespanRandomSeconds = TIMESPAN_LOOP_PEER_CONNECTOR_SECONDS / 2
            + randomGenerator.Next(TIMESPAN_LOOP_PEER_CONNECTOR_SECONDS);

          await Task.Delay(1000 * timespanRandomSeconds).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
          Log($"{ex.GetType().Name} in StartPeerConnector of protocol {Token}. Restart node."); 

          await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
      }
    }

    List<string> LoadIPAddresses(int maxCount, Random randomGenerator)
    {
      List<string> iPAddresses = new();

      if (IPAddresses.Count == 0)
      {
        IPAddresses = Token.GetSeedAddresses();

        foreach (FileInfo iPDisposed in DirectoryPeersDisposed.EnumerateFiles())
        {
          if (iPDisposed.Name.Contains(ConnectionType.OUTBOUND.ToString()))
          {
            int secondsBanned = TIMESPAN_PEER_BANNED_SECONDS -
              (int)(DateTime.Now - iPDisposed.CreationTime).TotalSeconds;

            if (0 < secondsBanned)
            {
              $"{iPDisposed.Name} is banned for {secondsBanned} seconds."
                .Log(this, Token.LogFile, Token.LogEntryNotifier);

              IPAddresses.RemoveAll(iP => iPDisposed.Name.Contains(iP));
              continue;
            }

            iPDisposed.MoveTo(Path.Combine(
              DirectoryPeersArchive.FullName,
              iPDisposed.Name));
          }
        }

        foreach (FileInfo fileIPAddressArchive in DirectoryPeersArchive.EnumerateFiles())
        {
          string iPFromFile = fileIPAddressArchive.Name.GetIPFromFileName();

          if (!IPAddresses.Any(ip => ip == iPFromFile))
            IPAddresses.Add(iPFromFile);
        }

        foreach (FileInfo fileIPAddressActive in DirectoryPeersActive.EnumerateFiles())
          IPAddresses.RemoveAll(iP => fileIPAddressActive.Name.GetIPFromFileName() == iP);
      }

      while (iPAddresses.Count < maxCount && IPAddresses.Count > 0)
      {
        int randomIndex = randomGenerator.Next(IPAddresses.Count);

        string iPAddress = IPAddresses[randomIndex];
        IPAddresses.RemoveAt(randomIndex);

        if (!Peers.Any(p => p.IPAddress.ToString() == iPAddress))
          iPAddresses.Add(iPAddress);
      }

      return iPAddresses;
    }

    void AddNetworkAddressesAdvertized(
      List<NetworkAddress> addresses)
    {
      foreach (NetworkAddress address in addresses)
      {
        string addressString = address.IPAddress.ToString();

        if (!IPAddresses.Contains(addressString))
          IPAddresses.Add(addressString);
      }
    }

    async Task CreatePeer(string iP)
    {
      try
      {
        Peer peer = new(
          this,
          Token.SizeBlockMax,
          IPAddress.Parse(iP),
          ConnectionType.OUTBOUND);

        await peer.Start();

        Peers.Add(peer);
      }
      catch (Exception ex)
      {
        $"Could not start {iP}: {ex.Message}".Log(this, LogEntryNotifier);
      }
    }

    async Task StartPeerInboundConnector()
    {
      TcpListener tcpListener = new(IPAddress.Any, Port);

      try
      {
        tcpListener.Start(COUNT_MAX_INBOUND_CONNECTIONS);
      }
      catch(Exception ex)
      {
        Log($"Failed to listen on port {Port}.\n {ex.Message}");
        return;
      }

      Log($"Start TCP listener on port {Port}.");

      while (true)
      {
        TcpClient tcpClient = await tcpListener.AcceptTcpClientAsync().ConfigureAwait(false);

        IPAddress remoteIP = ((IPEndPoint)tcpClient.Client.RemoteEndPoint).Address;

        if (remoteIP.ToString() != "84.74.69.100")
          continue;

        Log($"Received inbound request on port {Port} from {remoteIP}.");

        while (true)
        {
          lock (this)
            if (State == StateNetwork.Idle)
            {
              State = StateNetwork.ConnectingPeerInbound;
              break;
            }

          await Task.Delay(1000).ConfigureAwait(false);
        }

        Peer peer = null;

        lock (LOCK_Peers)
          peer = Peers.Find(p => p.IPAddress.Equals(remoteIP));

        if (peer != null)
        {
          Log($"Peer {peer} is already connected but received inbound connection request," +
            $"therefore initiate synchronization.");

          await peer.SendGetHeaders(Token.Network.GetLocator());

          continue;
        }

        lock (LOCK_Peers)
        {
          string rejectionString = "";

          if (Peers.Count(p => p.Connection == ConnectionType.INBOUND) >= COUNT_MAX_INBOUND_CONNECTIONS)
            rejectionString = $"Max number ({COUNT_MAX_INBOUND_CONNECTIONS}) of inbound connections reached.";

          foreach (FileInfo iPDisposed in DirectoryPeersDisposed.EnumerateFiles())
          {
            if (
              iPDisposed.Name.Contains(remoteIP.ToString()) &&
              iPDisposed.Name.Contains(ConnectionType.INBOUND.ToString()))
            {
              int secondsBanned = TIMESPAN_PEER_BANNED_SECONDS -
                (int)(DateTime.Now - iPDisposed.CreationTime).TotalSeconds;

              if (0 < secondsBanned)
              {
                rejectionString = $"{iPDisposed.Name} is banned for {secondsBanned} seconds.";
                break;
              }
            }
          }

          if (rejectionString == "")
          {
            peer = new(
              this,
              Token.SizeBlockMax,
              remoteIP,
              tcpClient,
              ConnectionType.INBOUND);

            Peers.Add(peer);

            $"Created inbound connection {peer}.".Log(this, Token.LogFile, Token.LogEntryNotifier);
          }
          else
          {
            $"Failed to create inbound peer {remoteIP}: \n{rejectionString}"
              .Log(this, Token.LogFile, Token.LogEntryNotifier);

            tcpClient.Dispose();

            lock (this)
              State = StateNetwork.Idle;

            continue;
          }
        }

        try
        {
          await peer.Start();
          Log($"Start inbound peer {peer}.");
        }
        catch (Exception ex)
        {
          Log($"Failed to connect to inbound peer {remoteIP}:\n" +
            $"{ex.GetType().Name}: {ex.Message}");

          peer.Dispose();

          lock (LOCK_Peers)
            Peers.Remove(peer);

          lock (this)
            State = StateNetwork.Idle;

          continue;
        }

        lock (this)
          State = StateNetwork.Idle;
      }
    }
  }
}
