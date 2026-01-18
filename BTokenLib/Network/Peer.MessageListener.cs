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
      public async Task SendVersionMessage()
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

        SetTimer("Await 'verack'.", TIMEOUT_HANDSHAKE_MILLISECONDS);

        State = StateProtocol.AwaitVerack;
      }

      public async Task StartStateMachine()
      {
        try
        {
          if (Connection == ConnectionType.OUTBOUND)
          {
            await SendVersionMessage();
          }
          else
          {
            SetTimer("Await 'version'.", TIMEOUT_HANDSHAKE_MILLISECONDS);

            State = StateProtocol.AwaitVersion;
          }

          while (true)
          {
            await RunNextMessage();
          }
        }
        catch (Exception ex)
        {
          Log($"{ex.GetType().Name} in listener: \n{ex.Message}");

          Dispose();
        }
      }

      async Task RunNextMessage()
      {
        byte[] magicByte = new byte[1];

        for (int i = 0; i < MagicBytes.Length; i++)
        {
          await ReadBytes(magicByte, 1);

          if (MagicBytes[i] != magicByte[0])
            i = magicByte[0] == MagicBytes[0] ? 0 : -1;
        }

        await ReadBytes(MessageHeader, MessageHeader.Length);

        string command = Encoding.ASCII.GetString(MessageHeader.Take(CommandSize).ToArray()).TrimEnd('\0');

        MessageNetwork messageNetworkOld = MessageNetworkCurrent;

        MessageNetworkCurrent = CommandsPeerProtocol[command];
        MessageNetworkCurrent.LengthDataPayload = BitConverter.ToInt32(MessageHeader, CommandSize);

        if (MessageNetworkCurrent.LengthDataPayload > MessageNetworkCurrent.Payload.Length)
          throw new ProtocolException($"Received network message payload length exceeds the allowed length of {MessageNetworkCurrent.Payload.Length} bytes for message {MessageNetworkCurrent.Command}.");

        await ReadBytes(MessageNetworkCurrent.Payload, MessageNetworkCurrent.LengthDataPayload);

        MessageNetworkCurrent.RunMessage(this, messageNetworkOld);
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

      public void SetTimer(string descriptionTimeOut = "", int millisecondsTimer = int.MaxValue)
      {
        if (descriptionTimeOut != "")
          Log($"Set timeout for '{descriptionTimeOut}' to {millisecondsTimer} ms.");

        Cancellation.CancelAfter(millisecondsTimer);
      }
    }
  }
}
