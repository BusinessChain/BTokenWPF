﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Threading.Tasks;

namespace BTokenLib
{
  partial class Network
  {
    const int TIMESPAN_LOOP_PEER_CONNECTOR_SECONDS = 10;
    const int TIMESPAN_PEER_BANNED_SECONDS = 10;
    int CountMaxPeers = 3;

    const int COUNT_MAX_INBOUND_CONNECTIONS = 3;

    enum StateNetwork
    {
      Idle,
      ConnectingPeerOutbound,
      ConnectingPeerInbound
    }
    StateNetwork State = StateNetwork.Idle;

    public enum ConnectionType { OUTBOUND, INBOUND };

    List<string> PoolIPAddress = new();



    public void IncrementCountMaxPeers()
    {
      CountMaxPeers++;
    }

    async Task StartPeerConnector()
    {
      Random randomGenerator = new();

      try
      {
        while (true)
        {
          while (true)
          {
            lock (this)
              if (State != StateNetwork.ConnectingPeerInbound)
              {
                State = StateNetwork.ConnectingPeerOutbound;
                break;
              }

            Task.Delay(2000).ConfigureAwait(false);
          }

          int countPeersCreate = CountMaxPeers - Peers.Count;

          if (countPeersCreate > 0)
          {
            List<string> iPAddresses = LoadIPAddresses(countPeersCreate, randomGenerator);

            if (iPAddresses.Count > 0)
            {
              var createPeerTasks = new Task[iPAddresses.Count];

              Parallel.For(
                0,
                iPAddresses.Count,
                i => createPeerTasks[i] = CreatePeer(iPAddresses[i]));

              await Task.WhenAll(createPeerTasks);
            }
          }

          lock (this)
            State = StateNetwork.Idle;

          int timespanRandomSeconds = TIMESPAN_LOOP_PEER_CONNECTOR_SECONDS / 2 
            + randomGenerator.Next(TIMESPAN_LOOP_PEER_CONNECTOR_SECONDS);

          await Task.Delay(1000 * timespanRandomSeconds).ConfigureAwait(false);
        }
      }
      catch (Exception ex)
      {
        $"{ex.GetType().Name} in StartPeerConnector of protocol {Token}. This is a bug."
          .Log(this, LogFile);
      }
    }

    List<string> LoadIPAddresses(int maxCount, Random randomGenerator)
    {
      List<string> iPAddresses = new();

      if (PoolIPAddress.Count == 0)
      {
        PoolIPAddress = Token.GetSeedAddresses();

        foreach (FileInfo iPDisposed in DirectoryPeersDisposed.EnumerateFiles())
        {
          if (iPDisposed.Name.Contains(ConnectionType.OUTBOUND.ToString()))
          {
            int secondsBanned = TIMESPAN_PEER_BANNED_SECONDS -
              (int)(DateTime.Now - iPDisposed.CreationTime).TotalSeconds;

            if (0 < secondsBanned)
            {
              $"{iPDisposed.Name} is banned for {secondsBanned} seconds.".Log(LogFile);
              PoolIPAddress.RemoveAll(iP => iPDisposed.Name.Contains(iP));
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

          if (!PoolIPAddress.Any(ip => ip == iPFromFile))
            PoolIPAddress.Add(iPFromFile);
        }

        foreach (FileInfo fileIPAddressActive in DirectoryPeersActive.EnumerateFiles())
          PoolIPAddress.RemoveAll(iP =>
          fileIPAddressActive.Name.GetIPFromFileName() == iP);
      }

      while (
        iPAddresses.Count < maxCount &&
        PoolIPAddress.Count > 0)
      {
        int randomIndex = randomGenerator.Next(PoolIPAddress.Count);

        iPAddresses.Add(PoolIPAddress[randomIndex]);
        PoolIPAddress.RemoveAt(randomIndex);
      }

      return iPAddresses;
    }

    void AddNetworkAddressesAdvertized(
      List<NetworkAddress> addresses)
    {
      foreach (NetworkAddress address in addresses)
      {
        string addressString = address.IPAddress.ToString();

        if (!PoolIPAddress.Contains(addressString))
          PoolIPAddress.Add(addressString);
      }
    }

    async Task CreatePeer(string iP)
    {
      Peer peer = null;

      try
      {
        lock (LOCK_Peers)
        {
          peer = Peers.Find(p => p.IPAddress.ToString() == iP);

          if (peer != null)
          {
            $"Connection with peer {peer} already established.".Log(LogFile);
            return;
          }

          peer = new(
            this,
            Token,
            IPAddress.Parse(iP),
            ConnectionType.OUTBOUND);

          Peers.Add(peer);
        }
      }
      catch (Exception ex)
      {
        $"{ex.GetType().Name} when creating peer {iP}:\n{ex.Message}.".Log(LogFile);
        return;
      }

      try
      {
        await peer.Connect();
      }
      catch (Exception ex)
      {
        $"Could not connect to {peer}: {ex.Message}".Log(LogFile);
        peer.Dispose();

        lock (LOCK_Peers)
          Peers.Remove(peer);
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
        $"Failed to listen on port {Port}.\n {ex.Message}".Log(LogFile);
        return;
      }

      $"Start TCP listener on port {Port}.".Log(this, LogFile);

      while (true)
      {
        TcpClient tcpClient = await tcpListener.AcceptTcpClientAsync()
          .ConfigureAwait(false);

        IPAddress remoteIP =
          ((IPEndPoint)tcpClient.Client.RemoteEndPoint).Address;

        if (remoteIP.ToString() != "84.74.69.100")
          continue;

        $"Received inbound request on port {Port} from {remoteIP}."
          .Log(this, LogFile);

        while (true)
        {
          lock (this)
            if (State == StateNetwork.Idle)
            {
              State = StateNetwork.ConnectingPeerInbound;
              break;
            }

          Task.Delay(1000).ConfigureAwait(false);
        }

        Peer peer = null;

        lock (LOCK_Peers)
        {
          try
          {
            string rejectionString = "";

            if (Peers.Count(p => p.Connection == ConnectionType.INBOUND) >= COUNT_MAX_INBOUND_CONNECTIONS)
              rejectionString = $"Max number ({COUNT_MAX_INBOUND_CONNECTIONS}) of inbound connections reached.";

            if (Peers.Any(p => p.IPAddress.Equals(remoteIP)))
              rejectionString = $"Connection already established.";

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

            if (rejectionString != "")
              throw new ProtocolException(rejectionString);

            peer = new(
              this,
              Token,
              remoteIP,
              tcpClient,
              ConnectionType.INBOUND);
          }
          catch (Exception ex)
          {
            ($"Failed to create inbound peer {remoteIP}: " +
              $"\n{ex.GetType().Name}: {ex.Message}")
              .Log(this, LogFile);

            tcpClient.Dispose();

            lock (this)
              State = StateNetwork.Idle;

            continue;
          }

          Peers.Add(peer);

          $"Created inbound connection {peer}.".Log(this, LogFile);
        }

        try
        {
          await peer.Connect();
          $"Connected to inbound peer {peer}.".Log(this, LogFile);
        }
        catch (Exception ex)
        {
          ($"Failed to connect to inbound peer {remoteIP}: " +
            $"\n{ex.GetType().Name}: {ex.Message}").Log(LogFile);

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
