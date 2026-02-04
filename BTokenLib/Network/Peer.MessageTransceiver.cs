using System;
using System.IO;
using System.Net;
using System.Linq;
using System.Text;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;


namespace BTokenLib
{
  partial class Network
  {
    partial class Peer
    {
      MessageNetworkProtocol MessageNetwork;

      public StateProtocol StateCurrent = StateProtocol.Handshake;


      public const int CommandSize = 12;
      public const int ChecksumSize = 4;

      static readonly byte[] MagicBytes = new byte[4] { 0xF9, 0xBE, 0xB4, 0xD9 };
      byte[] MagicBytesRead = new byte[4];
      byte[] CommandRead = new byte[CommandSize];
      byte[] LengthRead = new byte[4];
      byte[] ChecksumRead = new byte[ChecksumSize];

      readonly Dictionary<string, MessageNetworkProtocol> MessagesNetworkProtocol = new()
      {
        {"getheaders", new GetDataMessage()},
        {"headers", new HeadersMessage()}
      };

      async Task StartMessageReceiver()
      {
        try
        {
          while (true)
          {
            MessageNetworkProtocol message = await ReceiveMessageNext();
            message.Run(this);
          }
        }
        catch (Exception ex)
        {
          Dispose();
          Log($"{ex.GetType().Name} in message receiver: \n{ex.Message}");
        }
      }

      async Task ReadBytes(byte[] buffer, int bytesToRead)
      {
        int offset = 0;

        try
        {
          while (bytesToRead > 0)
          {
            int chunkSize = await NetworkStream.ReadAsync(
              buffer,
              offset,
              bytesToRead,
              Cancellation.Token)
              .ConfigureAwait(false);

            if (chunkSize == 0)
              throw new IOException("Stream returns 0 bytes signifying end of stream.");

            offset += chunkSize;
            bytesToRead -= chunkSize;
          }
        }
        catch (OperationCanceledException)
        {
          Log($"Timeout occured when waiting for next message.");
          throw;
        }
      }

      async Task<MessageNetworkProtocol> ReceiveMessageNext()
      {
        await ReadBytes(MagicBytesRead, 4);

        await ReadBytes(CommandRead, CommandRead.Length);
        string commandString = Encoding.ASCII.GetString(CommandRead).TrimEnd('\0');

        MessageNetworkProtocol message = MessagesNetworkProtocol[commandString];

        await ReadBytes(LengthRead, LengthRead.Length);
        message.LengthDataPayload = BitConverter.ToInt32(LengthRead);

        await ReadBytes(ChecksumRead, 4);

        byte[] bufferPayloadMessage = message.GetPayloadBuffer();

        await ReadBytes(bufferPayloadMessage, message.LengthDataPayload);

        return message;
      }


      SemaphoreSlim SemaphoreSendMessage = new(1);

      async Task SendMessage(MessageNetworkProtocol message)
      {
        await SemaphoreSendMessage.WaitAsync().ConfigureAwait(false);

        try
        {
          NetworkStream.Write(MagicBytes, 0, MagicBytes.Length);

          byte[] command = Encoding.ASCII.GetBytes(message.Command.PadRight(CommandSize, '\0'));
          NetworkStream.Write(command, 0, command.Length);

          byte[] payloadLength = BitConverter.GetBytes(message.LengthDataPayload);
          NetworkStream.Write(payloadLength, 0, payloadLength.Length);

          byte[] checksum = SHA256.ComputeHash(
            SHA256.ComputeHash(message.Payload, 0, message.LengthDataPayload));

          NetworkStream.Write(checksum, 0, ChecksumSize);

          NetworkStream.Write(message.Payload, 0, message.LengthDataPayload);
        }
        finally
        {
          SemaphoreSendMessage.Release();
        }
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
          MessageNetworkProtocol message = await ReceiveMessageNext();

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

      void SetTimer(string descriptionTimeOut = "", int millisecondsTimer = int.MaxValue)
      {
        if (descriptionTimeOut != "")
          Log($"Set timeout for '{descriptionTimeOut}' to {millisecondsTimer} ms.");

        Cancellation.CancelAfter(millisecondsTimer);
      }
    }
  }
}
