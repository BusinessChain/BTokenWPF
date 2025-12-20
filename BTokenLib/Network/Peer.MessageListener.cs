using System;
using System.IO;
using System.Linq;
using System.Text;
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

            if (Command == "headers")
            {
              $"Receiving headers message.".Log(this, LogFiles, Token.LogEntryNotifier);

              await ReadBytes(Payload, LengthDataPayload);

              int startIndex = 0;
              int countHeaders = VarInt.GetInt(Payload, ref startIndex);
              List<Header> headers = new();

              for (int i = 0; i < countHeaders; i += 1)
              {
                headers.Add(Token.ParseHeader(Payload, ref startIndex, SHA256));
                startIndex += 1; // Number of transaction in the block, which in the header is always 0
              }

              // Es wäre doch sinnvoller hier gerade die Payload zu übergeben, dann muss der Peer nicht
              // selber den Parser aufrufen.
              Network.TryReceiveHeaders(this, headers, ref flagMessageMayNotFollowConsensusRules);
            }
            else if (Command == "block")
            {
              if (HeaderDownload == null)
                throw new ProtocolException($"Received unrequested block message.");
              else
                //Subtract message count DoS metric.

              await ReadBytes(BlockDownload.Buffer, LengthDataPayload);
              BlockDownload.LengthBufferPayload = LengthDataPayload;

              BlockDownload.Parse(HeaderDownload.Height);

              $"Received block {BlockDownload}.".Log(this, LogFiles, Token.LogEntryNotifier);

              if (!BlockDownload.Header.Hash.IsAllBytesEqual(HeaderDownload.Hash))
                throw new ProtocolException(
                  $"Received unexpected block {BlockDownload} at height {BlockDownload.Header.Height} from peer {this}.\n" +
                  $"Requested was {HeaderDownload}.");

              ResetTimer();

              Network.InsertBlock(this, ref flagMessageMayNotFollowConsensusRules);
            }
            else if (Command == "tx")
            {
              byte[] tXRaw = new byte[LengthDataPayload];

              await ReadBytes(tXRaw, LengthDataPayload);

              TX tX = Token.ParseTX(tXRaw, SHA256);

              try
              {
                Token.InsertTXUnconfirmed(tX);
                $"Received TX {tX}.".Log(this, LogFiles, Token.LogEntryNotifier);
              }
              catch(Exception ex)
              {
                $"Rejected inbound TX {tX}\n {ex.GetType().Name}: {ex.Message}.".Log(this, LogFiles, Token.LogEntryNotifier);
              }
            }
            else if (Command == "getheaders")
            {
              await ReadBytes(Payload, LengthDataPayload);

              byte[] hashHeaderAncestor = new byte[32];

              int startIndex = 4;

              int headersCount = VarInt.GetInt(Payload, ref startIndex);

              $"Received getHeaders with {headersCount} locator hashes."
                .Log(this, LogFiles, Token.LogEntryNotifier);

              if (!Token.TryLock())
              {
                $"... but Token is locked.".Log(this, LogFiles, Token.LogEntryNotifier);
                continue;
              }

              List<Header> headers = new();

              Array.Copy(Payload, startIndex, hashHeaderAncestor, 0, 32);
              startIndex += 32;

              if (Network.TryLoadHeader(hashHeaderAncestor, out Header header))
              {
                $"In getheaders locator common ancestor is {header}."
                  .Log(this, LogFiles, Token.LogEntryNotifier);

                while (header.HeaderNext != null && headers.Count < 2000)
                {
                  headers.Add(header.HeaderNext);
                  header = header.HeaderNext;
                }

                if (headers.Any())
                  $"Send headers {headers.First()}...{headers.Last()}.".Log(this, LogFiles, Token.LogEntryNotifier);
                else
                  $"Send empty headers.".Log(this, LogFiles, Token.LogEntryNotifier);

                SendHeaders(headers);
              }

              Token.ReleaseLock();
            }
            else if (Command == "hashesDB")
            {
              await ReadBytes(Payload, LengthDataPayload);

              $"Receiving DB hashes.".Log(this, LogFiles, Token.LogEntryNotifier);

              //HashesDB = Token.ParseHashesDB(
              //  Payload,
              //  LengthDataPayload,
              //  HeaderchainDownload.HeaderTip);

              ResetTimer();

              Network.SyncDB(this);
            }
            else if (Command == "notfound")
            {
              await ReadBytes(Payload, LengthDataPayload);

              NotFoundMessage notFoundMessage = new(Payload);

              notFoundMessage.Inventories.ForEach(
                i => $"Did not find {i.Hash.ToHexString()}".Log(this, LogFiles, Token.LogEntryNotifier));
            }
            else if (Command == "inv")
            {
              await ReadBytes(Payload, LengthDataPayload);

              InvMessage invMessage = new(Payload);

              List<Inventory> inventoriesRequest = invMessage.Inventories.Where(
                i => i.IsTX() && !Token.TryGetTX(i.Hash, out TX tXInPool)).ToList();

              if (inventoriesRequest.Count > 0)
                SendMessage(new GetDataMessage(inventoriesRequest));
            }
            else if (Command == "getdata")
            {
              await ReadBytes(Payload, LengthDataPayload);

              GetDataMessage getDataMessage = new(Payload);

              foreach (Inventory inventory in getDataMessage.Inventories)
                if (inventory.Type == InventoryType.MSG_TX)
                {
                  if (Token.TryGetTX(inventory.Hash, out TX tXInPool))
                    await SendMessage(new TXMessage(tXInPool.TXRaw));
                  else
                    await SendMessage(new NotFoundMessage(
                      new List<Inventory>() { inventory }));
                }
                else if (inventory.Type == InventoryType.MSG_BLOCK)
                {
                  if (Network.TryLoadBlock(inventory.Hash, out Block block))
                  {
                    $"Send block {inventory}.".Log(this, LogFiles, Token.LogEntryNotifier);
                    await SendMessage(new MessageBlock(block.Buffer, block.LengthBufferPayload));
                  }
                  else
                    await SendMessage(new NotFoundMessage(
                      new List<Inventory>() { inventory }));
                }
                else if (inventory.Type == InventoryType.MSG_DB)
                {
                  if (Token.TryGetDB(inventory.Hash, out byte[] dataDB))
                    await SendMessage(new MessageDB(dataDB));
                }
                else
                  await SendMessage(new RejectMessage(inventory.Hash));
            }
            else if (Command == "dataDB")
            {
              await ReadBytes(Payload, LengthDataPayload);

              if (IsStateDBDownload())
              {
                byte[] hashDataDB = SHA256.ComputeHash(
                  Payload,
                  0,
                  LengthDataPayload);

                if (!hashDataDB.IsAllBytesEqual(HashDBDownload))
                  throw new ProtocolException(
                    $"Unexpected dataDB with hash {hashDataDB.ToHexString()}.\n" +
                    $"Excpected hash {HashDBDownload.ToHexString()}.");

                ResetTimer();

                //if (Network.InsertDB_FlagContinue(this))
                //  await RequestDB();
                //else
                //  SetStateIdle();
              }
            }
            else if (Command == "ping")
            {
              $"Received ping message.".Log(this, LogFiles, Token.LogEntryNotifier);

              await ReadBytes(Payload, LengthDataPayload);

              await SendMessage(new PongMessage(Payload));
            }
            else if (Command == "addr")
            {
              await ReadBytes(Payload, LengthDataPayload);
              AddressMessage addressMessage = new(Payload);

              Network.AddNetworkAddressesAdvertized(
                addressMessage.NetworkAddresses);
            }
            else if (Command == "sendheaders")
            {
              await SendMessage(new SendHeadersMessage());
            }
            else if (Command == "feefilter")
            {
              await ReadBytes(Payload, LengthDataPayload);

              FeeFilterMessage feeFilterMessage = new(Payload);
              FeeFilterValue = feeFilterMessage.FeeFilterValue;
            }
            else if (Command == "reject")
            {
              await ReadBytes(Payload, LengthDataPayload);

              RejectMessage rejectMessage = new(Payload);

              $"Get reject message: {rejectMessage.GetReasonReject()}".Log(this, LogFiles, Token.LogEntryNotifier);
            }
          }
        }
        catch (Exception ex)
        {
          $"{ex.GetType().Name} in listener: \n{ex.Message}".Log(this, LogFiles, Token.LogEntryNotifier);

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

        if (LengthDataPayload > Token.SizeBlockMax)
          throw new ProtocolException($"Message payload too big exceeding {Token.SizeBlockMax} bytes.");

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
          $"Set timeout for '{descriptionTimeOut}' to {millisecondsTimer} ms.".Log(this, LogFiles, Token.LogEntryNotifier);

        Cancellation.CancelAfter(millisecondsTimer);
      }
    }
  }
}
