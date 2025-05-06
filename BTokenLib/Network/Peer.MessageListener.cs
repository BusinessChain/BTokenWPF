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
        try
        {
          while (true)
          {
          LABEL_ListenForNextMessage:

            await ListenForNextMessage();

            if (Command == "block")
            {
              if (HeaderSync == null)
                throw new ProtocolException($"Received unrequested block message.");

              await ReadBytes(BlockSync.Buffer, LengthDataPayload);
              BlockSync.LengthBufferPayload = LengthDataPayload;

              BlockSync.Parse(HeaderSync.Height);

              $"Received block {BlockSync}".Log(this, LogFiles, Token.LogEntryNotifier);

              if (!BlockSync.Header.Hash.IsAllBytesEqual(HeaderSync.Hash))
                throw new ProtocolException(
                  $"Received unexpected block {BlockSync} at height {BlockSync.Header.Height} from peer {this}.\n" +
                  $"Requested was {HeaderSync}.");

              if (Token.TokenParent == null)
                Console.Beep(1200, 100);
              else
                Console.Beep(1500, 100);

              ResetTimer();

              Network.InsertBlock(BlockSync, HeaderSync.Height);

              SetStateIdle();
            }
            else if (Command == "tx")
            {
              byte[] tXRaw = new byte[LengthDataPayload];

              await ReadBytes(tXRaw, LengthDataPayload);

              TX tX = Token.ParseTX(tXRaw, SHA256);

              $"Received TX {tX}.".Log(this, LogFiles, Token.LogEntryNotifier);

              Token.InsertTXUnconfirmed(tX);
            }
            else if (Command == "headers")
            {
              await ReadBytes(Payload, LengthDataPayload);

              int startIndex = 0;
              int countHeaders = VarInt.GetInt(Payload, ref startIndex);

              $"Receiving {countHeaders} headers.".Log(this, LogFiles, Token.LogEntryNotifier);

              ResetTimer();

              if (!IsStateSync())
              {
                if (countHeaders != 1)
                  throw new ProtocolException($"Unsolicited headersMessage must contain exactly one header, but received {countHeaders}.");

                Header header = Token.ParseHeader(Payload, ref startIndex, SHA256);

                SetStateSync();

                try
                {
                  HeaderDownload.InsertHeader(header);
                }
                catch (ProtocolException)
                { }
              }
              else if (countHeaders > 0)
                for (int i = 0; i < countHeaders; i++)
                {
                  Header header = Token.ParseHeader(Payload, ref startIndex, SHA256);
                  startIndex += 1;

                  HeaderDownload.InsertHeader(header);
                }
              else if(countHeaders == 0)
              {
                $"Headerdownload completed, start block download.".Log(this, Token.LogFile, Token.LogEntryNotifier);

                SetStateIdle();

                Network.SyncBlocks(HeaderDownload);

                goto LABEL_ListenForNextMessage;
              }

              await SendGetHeaders(HeaderDownload.Locator);
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
                goto LABEL_ListenForNextMessage;
              }

              int i = 0;
              List<Header> headers = new();
              bool flagNotSameTipAsPeer = false;
              bool flagInitiateSynchronization = false;

              while (true)
              {
                Array.Copy(Payload, startIndex, hashHeaderAncestor, 0, 32);
                startIndex += 32;

                if (Token.TryGetHeader(hashHeaderAncestor, out Header header))
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
                  {
                    $"Send empty headers.".Log(this, LogFiles, Token.LogEntryNotifier);
                    flagInitiateSynchronization = flagNotSameTipAsPeer;
                  }

                  await SendHeaders(headers);

                  break;
                }
                else
                  flagNotSameTipAsPeer = true;

                if (i++ == headersCount)
                  throw new ProtocolException($"Found no common ancestor in getheaders locator.");
              }

              Token.ReleaseLock();

              if (flagInitiateSynchronization)
                await SendGetHeaders(Token.GetLocator());
            }
            else if (Command == "hashesDB")
            {
              await ReadBytes(Payload, LengthDataPayload);

              $"Receiving DB hashes.".Log(this, LogFiles, Token.LogEntryNotifier);

              HashesDB = Token.ParseHashesDB(
                Payload,
                LengthDataPayload,
                HeaderDownload.HeaderTip);

              ResetTimer();

              Network.SyncDB(this);
            }
            else if (Command == "notfound")
            {
              await ReadBytes(Payload, LengthDataPayload);

              NotFoundMessage notFoundMessage = new(Payload);

              notFoundMessage.Inventories.ForEach(
                i => $"Did not find {i.Hash.ToHexString()}".Log(this, LogFiles, Token.LogEntryNotifier));

              if (HeaderSync != null)
                Network.ReturnBlockDownloadIncomplete(HeaderSync);
            }
            else if (Command == "inv")
            {
              await ReadBytes(Payload, LengthDataPayload);

              InvMessage invMessage = new(Payload);

              List<Inventory> inventoriesRequest = invMessage.Inventories.Where(
                i => i.IsTX() && !Token.TXPool.TryGetTX(i.Hash, out TX tXInPool)).ToList();

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
                  if (Token.TXPool.TryGetTX(inventory.Hash, out TX tXInPool))
                    await SendMessage(new TXMessage(tXInPool.TXRaw));
                  else
                    await SendMessage(new NotFoundMessage(
                      new List<Inventory>() { inventory }));
                }
                else if (inventory.Type == InventoryType.MSG_BLOCK)
                {
                  if (Token.TryGetBlockBytes(inventory.Hash, out byte[] buffer))
                  {
                    $"Send block {inventory}.".Log(this, LogFiles, Token.LogEntryNotifier);
                    await SendMessage(new MessageBlock(buffer));
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

                if (Network.InsertDB_FlagContinue(this))
                  await RequestDB();
                else
                  SetStateIdle();
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

              $"Get reject message: {rejectMessage.GetReasonReject()}"
                .Log(this, LogFiles, Token.LogEntryNotifier);
            }
          }
        }
        catch (Exception ex)
        {
          $"{ex.GetType().Name} in listener: \n{ex.Message}".Log(this, LogFiles, Token.LogEntryNotifier);

          if (HeaderSync != null)
            Network.ReturnBlockDownloadIncomplete(HeaderSync);
          else if (IsStateDBDownload())
            Network.ReturnPeerDBDownloadIncomplete(HashDBDownload);

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
          throw new ProtocolException(
            $"Message payload too big exceeding {Token.SizeBlockMax} bytes.");

        Command = Encoding.ASCII.GetString(
          MessageHeader.Take(CommandSize).ToArray()).TrimEnd('\0');
      }

      void ResetTimer(string descriptionTimeOut = "", int millisecondsTimer = int.MaxValue)
      {
        if (descriptionTimeOut != "")
          $"Set timeout for '{descriptionTimeOut}' to {millisecondsTimer} ms.".Log(this, LogFiles, Token.LogEntryNotifier);

        Cancellation.CancelAfter(millisecondsTimer);
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
    }
  }
}
