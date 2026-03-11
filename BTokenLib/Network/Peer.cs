using System;
using System.IO;
using System.Net;
using System.Text;
using System.Linq;
using System.Threading;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Security.Cryptography;


namespace BTokenLib
{
  partial class Network
  {
    partial class Peer
    {
      public Network Network;

      const int TIMEOUT_RESPONSE_MILLISECONDS = 5000;

      public enum StateProtocol
      {
        Handshake,
        AwaitVersion,
        Idle,
        HeaderDownload,
        DBDownload,
        GetData,
        AdvertizingTX,
        Disposed,
        Busy
      }

      public Synchronization Synchronization;

      public byte[] HashDBDownload;
           
      const string UserAgent = "/BTokenCore:0.0.0/";
      public ConnectionType Connection;
      const UInt32 ProtocolVersion = 70015;
      public IPAddress IPAddress;
      TcpClient TcpClient;
      NetworkStream NetworkStream;
      CancellationTokenSource Cancellation = new();
      public const int TIMEOUT_HANDSHAKE_MILLISECONDS = 5000;

      public SHA256 SHA256 = SHA256.Create();

      public ILogEntryNotifier LogEntryNotifier;
      StreamWriter LogFile;

      public DateTime TimePeerCreation = DateTime.Now;


      public Peer(
        Network network,
        int sizeBlockMax,
        IPAddress ip,
        TcpClient tcpClient,
        ConnectionType connection)
      {
        Network = network;

        TcpClient = tcpClient;
        IPAddress = ip;
        Connection = connection;

        CreateLogFile($"{ip}-{Connection}");
      }

      void CreateLogFile(string name)
      {
        string pathLogFileActive = Path.Combine(
          Network.DirectoryPeersActive.FullName,
          name);

        if (File.Exists(pathLogFileActive))
          throw new ProtocolException($"Peer {this} already active.");

        string pathLogFileDisposed = Path.Combine(
          Network.DirectoryPeersDisposed.FullName,
          name);

        if (File.Exists(pathLogFileDisposed))
        {
          TimeSpan secondsSincePeerDisposal = TimePeerCreation - File.GetLastWriteTime(pathLogFileDisposed);
          int secondsBannedRemaining = TIMESPAN_PEER_BANNED_SECONDS - (int)secondsSincePeerDisposal.TotalSeconds;

          if (secondsBannedRemaining > 0)
            throw new ProtocolException(
              $"Peer {this} is banned for {secondsBannedRemaining} seconds.");

          File.Move(pathLogFileDisposed, pathLogFileActive);
        }

        string pathLogFileArchive = Path.Combine(
          Network.DirectoryPeersArchive.FullName,
          name);

        if (File.Exists(pathLogFileArchive))
          File.Move(pathLogFileArchive, pathLogFileActive);

        LogFile = new StreamWriter(
          pathLogFileActive,
          append: true);
      }

      public async Task Start()
      {
        Log($"Start peer - {Connection}.");

        if (!TcpClient.Connected)
          await TcpClient.ConnectAsync(IPAddress, Network.Port).ConfigureAwait(false);

        NetworkStream = TcpClient.GetStream();

        await Handshake();

        StartMessageReceiver();

        if (Connection == ConnectionType.OUTBOUND)
          SendGetHeaders(Network.GetLocator());
      }

      public void BroadcastTX(TX tX)
      {
        InvMessage invMessage = new(new List<Inventory> {
            new(InventoryType.MSG_TX, tX.Hash)});

        SendMessage(invMessage);
      }

      void RequestBlock()
      {
        (MessagesNetworkProtocol["block"] as BlockMessage)
          .RequestBlock(this);
      }

      async Task SendVersion()
      {
        await SendMessage(new VersionMessage(
              protocolVersion: ProtocolVersion,
              networkServicesLocal: 0,
              unixTimeSeconds: DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
              networkServicesRemote: 0,
              iPAddressRemote: IPAddress.Loopback,
              portRemote: Network.Port,
              iPAddressLocal: IPAddress.Loopback,
              portLocal: Network.Port,
              nonce: 0,
              userAgent: UserAgent,
              blockchainHeight: 0,
              relayOption: 0x01));
      }

      async Task SendGetHeaders(List<Header> locator)
      {
        SetTimer("Get headers.", TIMEOUT_RESPONSE_MILLISECONDS);

        try
        {
          await SendMessage(new GetHeadersMessage(locator, ProtocolVersion));
          $"Send getheaders. Locator: {locator.First()} ... {locator.Last()}".Log(this, LogFile, Network.LogEntryNotifier);
        }
        catch (Exception ex)
        {
          $"Exception {ex.GetType().Name} when sending getheaders message.".Log(this, LogFile, Network.LogEntryNotifier);
        }
      }

      public async Task AdvertizeTX(TX tX)
      {
        $"Advertize token {tX}.".Log(this, LogFile, Network.LogEntryNotifier);

        InvMessage invMessage = new(new List<Inventory> {
          new(InventoryType.MSG_TX, tX.Hash)
        });

        await SendMessage(invMessage);
      }
      
      public async Task RequestDB()
      {
        $"Start downloading DB {HashDBDownload.ToHexString()}."
          .Log(this, LogFile, Network.LogEntryNotifier);

        State = StateProtocol.DBDownload;

        SetTimer("receive DB", TIMEOUT_RESPONSE_MILLISECONDS);

        await SendMessage(new GetDataMessage(InventoryType.MSG_DB, HashDBDownload));
      }

      public void Dispose()
      {
        Log($"Dispose {Connection}.");

        Cancellation.Cancel();

        TcpClient.Dispose();

        LogFile.Dispose();

        string pathLogFile = ((FileStream)LogFile.BaseStream).Name;
        string nameLogFile = Path.GetFileName(pathLogFile);
        string pathLogFileDisposed = Path.Combine(
          Network.DirectoryPeersDisposed.FullName, nameLogFile);

        File.Move(pathLogFile, pathLogFileDisposed);
        File.SetCreationTime(pathLogFileDisposed, DateTime.Now);
      }

      public string GetStatus()
      {
        int lifeTime = (int)(DateTime.Now - TimePeerCreation).TotalMinutes;

        lock (this)
          return
            $"\nStatus peer {this}:\n" +
            $"lifeTime minutes: {lifeTime}\n" +
            $"Connection: {Connection}\n";
      }

      public void Log(string messageLog)
      {
        messageLog.Log(this, LogEntryNotifier);
      }

      public override string ToString()
      {
        return $"{Network} [{IPAddress}|{Connection}]";
      }
    }
  }
}
