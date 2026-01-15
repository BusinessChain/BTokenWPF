using System;
using System.IO;
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
      public async Task StartMessageListener()
      {
        // innerhalb 10 minuten max 5 (empirisch bestimmen) non_bulk messages akzeptieren
        bool flagMessageMayNotFollowConsensusRules = false;

        try
        {
          while (true)
          {
            // Divide messages into solicited and unsolicited messages.

            // Solicited messages are messages that we asked for. These messages are by definition never spam.
            // If a solicited message contains data that violates the protocol, a ProtocolException should be thrown,
            // in which case the peer is disconnected for 24 hours. This mey happen even when the peer had no malicious intent,
            // but that doesnt matter, as connections to peers should be randomized and continuously shuffled anyway,
            // because the network layer should be kept trustless.

            // Unsolicited messages may be blocks, transactions, protocol command.
            // Here we should deploy a DoS throttle for each category. 
            // Since blocks on average can be created only one every 10 minutes, 
            // We can be generous an allow an average of one block message per minute over a ten minute window.
            // With transactions, we can accept a tlps [transaction load per second] of G * Blocksize / 600 [Bytes/s]
            // G is some generosity margins such that temporary burst demand may be satisfied.
            // In BToken that would be 1'000'000 / 600 = 1.6 kB/s worth of transaction. We can bump this up to 5 kB/s
            // worth of transaction that we accept, if a peer send us more, we will disconnect.
            // DoS limiters for protocol commands have to be assessed seperately for each command.

            // Bezüglich log file per peer: Das logfile der peers gibt es nur im debug modus.
            if(flagMessagePossible_DoS_Attack)
            {
              // increment DoS counter
              // introduce a fixed Message/time metric for DoS detection. The network protocol
              // shoud be designed in such a way that a few messages per minutes are enough.
              // Assuming there are on average 

              //if (counterPossibleDOSMessages > max)
              //  throw new ProtocolException("Received too many possible DoS messages from peer.");
            }

            flagMessageMayNotFollowConsensusRules = true;

            await ListenForNextMessage();

            if (!CommandsPeerProtocol.TryGetValue(Command, out MessageNetwork messageNetwork))
                continue;

            await ReadBytes(messageNetwork.Payload, LengthDataPayload);

            messageNetwork.RunMessage(this);
          }
        }
        catch (Exception ex)
        {
          $"{ex.GetType().Name} in listener: \n{ex.Message}".Log(this, LogFile, Network.LogEntryNotifier);

          Dispose();
        }
      }

      async Task ListenForNextMessage()
      {
        byte[] magicByte = new byte[1];

        for (int i = 0; i < MagicBytes.Length; i++)
        {
          await ReadBytes(magicByte, 1);

          if (MagicBytes[i] != magicByte[0])
            i = magicByte[0] == MagicBytes[0] ? 0 : -1;
        }

        await ReadBytes(MessageHeader, MessageHeader.Length);

        LengthDataPayload = BitConverter.ToInt32(MessageHeader, CommandSize);

        if (LengthDataPayload > Payload.Length)
          throw new ProtocolException($"Message payload too big exceeding {Payload.Length} bytes.");

        Command = Encoding.ASCII.GetString(MessageHeader.Take(CommandSize).ToArray()).TrimEnd('\0');
      }

      async Task ReadBytes(byte[] buffer, int bytesToRead)
      {
        int offset = 0;

        while (bytesToRead > 0)
        {
          int chunkSize = await NetworkStream.ReadAsync(
            buffer,
            offset,
            bytesToRead,
            Cancellation.Token)
            .ConfigureAwait(false);

          if (chunkSize == 0)
            throw new IOException(
              "Stream returns 0 bytes signifying end of stream.");

          offset += chunkSize;
          bytesToRead -= chunkSize;
        }
      }

      void ResetTimer(string descriptionTimeOut = "", int millisecondsTimer = int.MaxValue)
      {
        if (descriptionTimeOut != "")
          $"Set timeout for '{descriptionTimeOut}' to {millisecondsTimer} ms.".Log(this, LogFile, Network.LogEntryNotifier);

        Cancellation.CancelAfter(millisecondsTimer);
      }
    }
  }
}
