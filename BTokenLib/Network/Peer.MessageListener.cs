﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace BTokenLib
{
  partial class Network
  {
    partial class Peer
    {
      bool FlagSingleBlockDownload;

      public async Task StartMessageListener()
      {
        try
        {
          while (true)
          {
            await ListenForNextMessage();

            if (Command == "block")
            {
              if (!IsStateBlockSynchronization())
                throw new ProtocolException($"Received unrequested block message.");

              await ReadBytes(Block.Buffer, LengthDataPayload);

              Block.Parse();

              $"Peer received block {Block}".Log(this, LogFile);

              if (!Block.Header.Hash.IsEqual(HeaderSync.Hash))
                throw new ProtocolException(
                  $"Received unexpected block {Block} at height {Block.Header.Height} from peer {this}.\n" +
                  $"Requested was {HeaderSync}.");

              if (Token.TokenParent == null)
                Console.Beep(1200, 100);
              else
                Console.Beep(1500, 100);

              ResetTimer();

              if (FlagSingleBlockDownload)
              {
                FlagSingleBlockDownload = false;

                Token.InsertBlock(Block);
                Network.ExitSynchronization();

                if (Block.BlockChild != null)
                  Token.TokenChild.Network.AdvertizeBlockToNetwork(Block.BlockChild);
                else if (Token.TokenChild != null)
                  Token.TokenChild.Network.TryStartSynchronization();
              }
              else if (Network.InsertBlock_FlagContinue(this))
                RequestBlock();
              else
                SetStateIdle();
            }
            else if (Command == "tx")
            {
              await ReadBytes(Payload, LengthDataPayload);

              int index = 0;

              TX tX = Block.ParseTX(
                isCoinbase: false,
                Payload,
                ref index,
                SHA256.Create());

              tX.TXRaw = Payload.Take(index).ToList();

              $"Received TX {tX}.".Log(LogFile);

              if (!Token.TXPool.TryGetTX(tX.Hash, out TX tXInPool))
                Token.TXPool.TryAddTX(tX);
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

                if (!hashDataDB.IsEqual(HashDBDownload))
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
              $"Received ping message.".Log(LogFile);

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
            else if (Command == "headers")
            {
              await ReadBytes(Payload, LengthDataPayload);

              int byteIndex = 0;

              int countHeaders = VarInt.GetInt32(
                Payload,
                ref byteIndex);

              $"{this}: Receiving {countHeaders} headers.".Log(LogFile);

              if (IsStateHeaderSynchronization())
              {
                if (countHeaders > 0)
                {
                  Header header = null;
                  List<Header> locator = null;

                  int i = 0;
                  while (i < countHeaders)
                  {
                    header = Block.ParseHeader(
                      Payload,
                      ref byteIndex);

                    byteIndex += 1;

                    Network.HeaderDownload.InsertHeader(header);

                    i += 1;

                    if (i == countHeaders)
                      locator = new List<Header> { header };
                  }

                  await SendGetHeaders(locator);
                }
                else
                {
                  ResetTimer();

                  Network.SyncBlocks();
                }
              }
              else
              {
                if (countHeaders != 1)
                  throw new ProtocolException($"Peer sent unsolicited not exactly one header.");

                if (!Network.TryEnterStateSynchronization(this))
                  continue;

                Header header = Block.ParseHeader(
                  Payload,
                  ref byteIndex);

                if (header.HashPrevious.IsEqual(Token.HeaderTip.Hash))
                {
                  header.AppendToHeader(Token.HeaderTip);

                  FlagSingleBlockDownload = true;
                  await RequestBlock(header);
                }
                else
                {
                  Header headerPrevious = Token.HeaderTip.HeaderPrevious;

                  while (true)
                  {
                    if (headerPrevious == null)
                    {
                      await SendGetHeaders(Token.GetLocator());
                      break;
                    }

                    if (header.HashPrevious.IsEqual(headerPrevious.Hash))
                    {
                      if (!header.HashPrevious.IsEqual(Token.HeaderTip.HashPrevious))
                        await SendHeaders(new List<Header>() { Token.HeaderTip });

                      Network.ExitSynchronization();
                      break;
                    }

                    headerPrevious = headerPrevious.HeaderPrevious;
                  }
                }
              }
            }
            else if (Command == "getheaders")
            {
              await ReadBytes(Payload, LengthDataPayload);

              byte[] hashHeaderAncestor = new byte[32];

              int startIndex = 4;

              int headersCount = VarInt.GetInt32(Payload, ref startIndex);

              $"Peer {this} received getHeaders with {headersCount} locator hashes."
                .Log(LogFile);

              if (!Token.TryLock())
              {
                $"... but Token is locked.".Log(LogFile);
                continue;
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
                    .Log(this, LogFile);

                  while (header.HeaderNext != null && headers.Count < 2000)
                  {
                    headers.Add(header.HeaderNext);
                    header = header.HeaderNext;
                  }

                  if (headers.Any())
                    $"Send headers {headers.First()}...{headers.Last()}.".Log(this, LogFile);
                  else
                  {
                    $"Send empty headers.".Log(this, LogFile);
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

              if (flagInitiateSynchronization && Network.TryEnterStateSynchronization(this))
                await SendGetHeaders(Token.GetLocator());
            }
            else if (Command == "hashesDB")
            {
              await ReadBytes(Payload, LengthDataPayload);

              $"{this}: Receiving DB hashes.".Log(LogFile);

              HashesDB = Token.ParseHashesDB(
                Payload,
                LengthDataPayload,
                Network.HeaderDownload.HeaderTip);

              ResetTimer();

              Network.SyncDB(this);
            }
            else if (Command == "notfound")
            {
              await ReadBytes(Payload, LengthDataPayload);

              NotFoundMessage notFoundMessage = new(Payload);

              notFoundMessage.Inventories.ForEach(
                i => $"Did not find {i.Hash.ToHexString()}".Log(this, LogFile));

              if (IsStateBlockSynchronization())
                Network.ReturnPeerBlockDownloadIncomplete(this);
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
                    await SendMessage(new TXMessage(tXInPool.TXRaw.ToArray()));
                  else
                    await SendMessage(new NotFoundMessage(
                      new List<Inventory>() { inventory }));
                }
                else if (inventory.Type == InventoryType.MSG_BLOCK)
                {
                  if (Token.TryGetBlockBytes(inventory.Hash, out byte[] buffer))
                  {
                    $"Send block {inventory}.".Log(LogFile);
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
            else if (Command == "reject")
            {
              await ReadBytes(Payload, LengthDataPayload);

              RejectMessage rejectMessage = new(Payload);

              $"Peer {this} gets reject message: {rejectMessage.GetReasonReject()}"
                .Log(this, LogFile);
            }
          }
        }
        catch (Exception ex)
        {
          Network.HandleExceptionPeerListener(this);
          $"{ex.GetType().Name} in listener: \n{ex.Message}".Log(LogFile);
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

        await ReadBytes(
          MessageHeader,
          MessageHeader.Length);

        LengthDataPayload = BitConverter.ToInt32(
          MessageHeader,
          CommandSize);

        if (LengthDataPayload > SIZE_MESSAGE_PAYLOAD_BUFFER)
          throw new ProtocolException(
            $"Message payload too big exceeding " +
            $"{SIZE_MESSAGE_PAYLOAD_BUFFER} bytes.");

        Command = Encoding.ASCII.GetString(
          MessageHeader.Take(CommandSize)
          .ToArray()).TrimEnd('\0');
      }

      async Task ReadBytes(
        byte[] buffer,
        int bytesToRead)
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
