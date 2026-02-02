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
      Network Network;

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

      public Header HeaderDownload;
      public Block BlockDownload;

      public byte[] HashDBDownload;
      public List<byte[]> HashesDB;
           
      ulong FeeFilterValue;

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

      public int HeightHeaderTipLastCommunicated;


      public Peer(
        Network network,
        int sizeBlockMax,
        IPAddress ip,
        ConnectionType connection) : this(
          network,
          sizeBlockMax,
          ip,
          new TcpClient(),
          connection)
      { }

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

        StartMessageListener();

        if (Connection == ConnectionType.OUTBOUND)
          SendGetHeaders(Network.GetLocator()); // GetLocator() should contain a lock.
      }

      async Task Handshake()
      {
        SetTimer("Timeout handshake.", TIMEOUT_HANDSHAKE_MILLISECONDS);

        if (Connection == ConnectionType.OUTBOUND)
          SendVersion();

        bool flagReceivedVersion = false;
        bool flagReceivedVerack = false;

        while (!flagReceivedVersion || !flagReceivedVerack)
        {
          MessageNetworkProtocol message = await ReceiveNextMessage();

          if (message.Command == "verack")
          {
            flagReceivedVerack = true;
          }
          else if (message.Command == "version")
          {
            flagReceivedVersion = true;
            SendMessage(new VerAckMessage());

            if (Connection == ConnectionType.INBOUND)
              SendVersion();
          }
        }
      }

      public void StartBlockDownload()
      {
        if (!Network.TryFetchHeaderDownload(out Header headerDownload))
          return;

        if (!Network.PoolBlocks.TryTake(out Block blockDownload))
          blockDownload = new Block(Network.Token);

        BlockMessage blockMessage = MessagesNetworkProtocol["block"] as BlockMessage;

        blockMessage.HeaderDownload = headerDownload;
        blockMessage.BlockDownload = blockDownload;

        Log($"Start downloading block {blockDownload}.");

        SetTimer("Receive block", TIMEOUT_RESPONSE_MILLISECONDS);

        SendMessage(new GetDataMessage(InventoryType.MSG_BLOCK, headerDownload.Hash));
      }

      void InsertBlock(Block block)
      {
        Network.InsertBlock(block);
      }

      SemaphoreSlim SemaphoreSendMessage = new(1);

      async Task SendMessage(MessageNetworkProtocol message)
      {
        await SemaphoreSendMessage.WaitAsync().ConfigureAwait(false);
        
        try
        {
          NetworkStream.Write(
            MessageNetworkProtocol.MagicBytes, 
            0, 
            MessageNetworkProtocol.MagicBytes.Length);

          byte[] command = Encoding.ASCII.GetBytes(
            message.Command.PadRight(MessageNetworkProtocol.CommandSize, '\0'));
          NetworkStream.Write(command, 0, command.Length);

          byte[] payloadLength = BitConverter.GetBytes(message.LengthDataPayload);
          NetworkStream.Write(payloadLength, 0, payloadLength.Length);

          byte[] checksum = SHA256.ComputeHash(
            SHA256.ComputeHash(
              message.Payload,
              message.OffsetPayload,
              message.LengthDataPayload));

          NetworkStream.Write(checksum, 0, MessageNetworkProtocol.ChecksumSize);

          NetworkStream.Write(
            message.Payload,
            message.OffsetPayload,
            message.LengthDataPayload);
        }
        finally
        {
          SemaphoreSendMessage.Release();
        }
      }

      public void BroadcastTX(TX tX)
      {
        InvMessage invMessage = new(new List<Inventory> {
            new(InventoryType.MSG_TX, tX.Hash)});

        SendMessage(invMessage);
      }

      public async Task SendVersion()
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

      public async Task SendGetHeaders(List<Header> locator)
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

      public async Task RequestBlock(Header headerDownload, Block blockDownload)
      {
        BlockMessage blockMessage = MessagesNetworkProtocol["block"] as BlockMessage;

        HeaderDownload = headerDownload;
        blockMessage.BlockDownload = blockDownload;

        Log($"Start downloading block {blockDownload}.");

        SetTimer("Receive block", TIMEOUT_RESPONSE_MILLISECONDS);

        await SendMessage(new GetDataMessage(InventoryType.MSG_BLOCK, headerDownload.Hash));
      }

      public async Task SendHeaders(List<Header> headers)
      {
        string logText = $"Send {headers.Count} headers.";

        if (headers.Count > 0)
          logText += $" {headers.First()}";
        if (headers.Count > 1)
          logText += $" ... {headers.Last()}.";

        logText.Log(this, LogFile, Network.LogEntryNotifier);

        await SendMessage(new HeadersMessage(headers));
      }

      public async Task AdvertizeBlock(Block block)
      {
        lock(this)
        {
          if(State != StateProtocol.Idle)
          {
            $"Is not idle when attempting to send block {block} but in state {State}."
              .Log(this, LogFile, Network.LogEntryNotifier);
            return;
          }

          State = StateProtocol.HeaderDownload;
        }

        $"Advertize block {block}.".Log(this, LogFile, Network.LogEntryNotifier);

        await SendHeaders(new List<Header>() { block.Header });

        SetStateIdle();
      }

      public bool TryRequestIdlePeer()
      {
        lock (this)
          if (State == StateProtocol.Idle)
          {
            State = StateProtocol.Busy;
            return true;
          }

        return false;
      }

      public bool IsStateIdle()
      {
        lock (this)
          return State == StateProtocol.Idle;
      }

      public void SetStateIdle()
      {
        lock (this)
          State = StateProtocol.Idle;
      }

      public bool IsStateSync()
      {
        lock (this)
          return State == StateProtocol.HeaderDownload;
      }

      bool IsStateAwaitingGetDataTX()
      {
        lock (this)
          return State == StateProtocol.GetData;
      }

      public bool IsStateDBDownload()
      {
        lock (this)
          return State == StateProtocol.DBDownload;
      }

      public void Dispose()
      {
        $"Dispose {Connection}".Log(this, LogFile, Network.LogEntryNotifier);

        Cancellation.Cancel();

        TcpClient.Dispose();

        LogFile.Dispose();

        State = StateProtocol.Disposed;

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
            $"State: {State}\n" +
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
