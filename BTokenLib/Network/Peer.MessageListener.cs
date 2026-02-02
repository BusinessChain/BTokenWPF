using System;
using System.IO;
using System.Net;
using System.Linq;
using System.Text;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Collections.Generic;


namespace BTokenLib
{
  partial class Network
  {
    partial class Peer
    {
      MessageNetworkProtocol MessageNetwork;

      Dictionary<StateProtocol, StateProtocolPeer> StatesPeerProtocol = new()
      {
        { StateProtocol.Handshake, new StateHandshake() },
        { StateProtocol.Idle, new StateIdle() }
      };

      public StateProtocol StateCurrent = StateProtocol.Handshake;


      async Task StartMessageListener()
      {
        try
        {
          while (true)
          {
            MessageNetworkProtocol message = await ReceiveNextMessage();
            message.Run(this);
          }
        }
        catch (Exception ex)
        {
          Log($"{ex.GetType().Name} in state {StateCurrent.ToString()}: \n{ex.Message}");
          Dispose();
        }
      }

      static readonly byte[] MagicBytes = new byte[4] { 0xF9, 0xBE, 0xB4, 0xD9 };
      readonly Dictionary<string, MessageNetworkProtocol> MessagesNetworkProtocol = new()
      {
        {"getheaders", new GetDataMessage()},
        {"headers", new HeadersMessage() }
      };

      async Task<MessageNetworkProtocol> ReceiveNextMessage()
      {
        byte[] byteFromStream = new byte[4];
        await ReadBytes(byteFromStream, 4);

        if (!byteFromStream.IsAllBytesEqual(MagicBytes))
          throw new ProtocolException($"Did receive something else insead of magic word 'f9 be b4 d9'.");

        byte[] command = new byte[12];
        await ReadBytes(command, command.Length);
        string commandString = Encoding.ASCII.GetString(command).TrimEnd('\0');

        MessageNetworkProtocol message = MessagesNetworkProtocol[commandString];

        byte[] lenght = new byte[4];
        await ReadBytes(lenght, lenght.Length);
        uint lengthDataPayload = BitConverter.ToUInt32(lenght);

        if (lengthDataPayload > message.Payload.Length)
          throw new ProtocolException($"Received network message payload length " +
            $"exceeds the allowed length of {message.Payload.Length} bytes for message {commandString}.");
        
          byte[] checksum = new byte[4];
        await ReadBytes(checksum, 4);

        await ReadBytes(message.Payload, (int)lengthDataPayload);
        message.LengthDataPayload = (int)lengthDataPayload;

        message.ParsePayload();

        return message;
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

      void SetTimer(string descriptionTimeOut = "", int millisecondsTimer = int.MaxValue)
      {
        if (descriptionTimeOut != "")
          Log($"Set timeout for '{descriptionTimeOut}' to {millisecondsTimer} ms.");

        Cancellation.CancelAfter(millisecondsTimer);
      }
    }
  }
}
