using System;
using System.Collections.Generic;
using System.Security.Cryptography;


namespace BTokenLib
{
  public partial class Network
  {
    void ReceivedCommand(string command, Peer peer)
    {
      if (command == "headers")
      {
        $"Receiving headers message.".Log(this, LogFile, Token.LogEntryNotifier);

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
      else if (command == "block")
      {
        if (HeaderDownload == null)
          throw new ProtocolException($"Received unrequested block message.");
        //else
        //Subtract message count DoS metric.

        BlockDownload.LengthBufferPayload = LengthDataPayload;

        BlockDownload.Parse(HeaderDownload.Height);

        $"Received block {BlockDownload}.".Log(this, LogFile, Token.LogEntryNotifier);

        if (!BlockDownload.Header.Hash.IsAllBytesEqual(HeaderDownload.Hash))
          throw new ProtocolException(
            $"Received unexpected block {BlockDownload} at height {BlockDownload.Header.Height} from peer {this}.\n" +
            $"Requested was {HeaderDownload}.");

        ResetTimer();

        Network.InsertBlock(this, ref flagMessageMayNotFollowConsensusRules);
      }
      else if (command == "tx")
      {
        TX tX = Token.ParseTX(tXRaw, SHA256);

        try
        {
          Token.InsertTXUnconfirmed(tX);
          $"Received TX {tX}.".Log(this, LogFile, Token.LogEntryNotifier);
        }
        catch (Exception ex)
        {
          $"Rejected inbound TX {tX}\n {ex.GetType().Name}: {ex.Message}.".Log(this, LogFile, Token.LogEntryNotifier);
        }
      }
      else if (command == "getheaders")
      {
        byte[] hashHeaderAncestor = new byte[32];

        int startIndex = 4;

        int headersCount = VarInt.GetInt(Payload, ref startIndex);

        $"Received getHeaders with {headersCount} locator hashes."
          .Log(this, LogFile, Token.LogEntryNotifier);

        if (!Token.TryLock())
        {
          $"... but Token is locked.".Log(this, LogFile, Token.LogEntryNotifier);
          continue;
        }

        List<Header> headers = new();

        Array.Copy(Payload, startIndex, hashHeaderAncestor, 0, 32);
        startIndex += 32;

        if (Network.TryLoadHeader(hashHeaderAncestor, out Header header))
        {
          $"In getheaders locator common ancestor is {header}."
            .Log(this, LogFile, Token.LogEntryNotifier);

          while (header.HeaderNext != null && headers.Count < 2000)
          {
            headers.Add(header.HeaderNext);
            header = header.HeaderNext;
          }

          if (headers.Any())
            $"Send headers {headers.First()}...{headers.Last()}.".Log(this, LogFile, Token.LogEntryNotifier);
          else
            $"Send empty headers.".Log(this, LogFile, Token.LogEntryNotifier);

          SendHeaders(headers);
        }

        Token.ReleaseLock();
      }
      else if (command == "hashesDB")
      {
        $"Receiving DB hashes.".Log(this, LogFile, Token.LogEntryNotifier);

        //HashesDB = Token.ParseHashesDB(
        //  Payload,
        //  LengthDataPayload,
        //  HeaderchainDownload.HeaderTip);

        ResetTimer();

        Network.SyncDB(this);
      }
      else if (command == "notfound")
      {
        NotFoundMessage notFoundMessage = new(Payload);

        notFoundMessage.Inventories.ForEach(
          i => $"Did not find {i.Hash.ToHexString()}".Log(this, LogFile, Token.LogEntryNotifier));
      }
      else if (command == "inv")
      {
        InvMessage invMessage = new(Payload);

        List<Inventory> inventoriesRequest = invMessage.Inventories.Where(
          i => i.IsTX() && !Token.TryGetTX(i.Hash, out TX tXInPool)).ToList();

        if (inventoriesRequest.Count > 0)
          SendMessage(new GetDataMessage(inventoriesRequest));
      }
      else if (command == "getdata")
      {
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
              $"Send block {inventory}.".Log(this, LogFile, Token.LogEntryNotifier);
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
      else if (command == "dataDB")
      {
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
      else if (command == "ping")
      {
        $"Received ping message.".Log(this, LogFile, Token.LogEntryNotifier);

        await SendMessage(new PongMessage(Payload));
      }
      else if (command == "addr")
      {
        AddressMessage addressMessage = new(Payload);

        foreach (NetworkAddress address in addressMessage.NetworkAddresses)
        {
          string addressString = address.IPAddress.ToString();

          if (!IPAddresses.Contains(addressString))
            IPAddresses.Add(addressString);
        }
      }
      else if (command == "sendheaders")
      {
        await SendMessage(new SendHeadersMessage());
      }
      else if (command == "feefilter")
      {
        FeeFilterMessage feeFilterMessage = new(Payload);
        FeeFilterValue = feeFilterMessage.FeeFilterValue;
      }
      else if (command == "reject")
      {
        RejectMessage rejectMessage = new(Payload);

        $"Get reject message: {rejectMessage.GetReasonReject()}".Log(this, LogFile, Token.LogEntryNotifier);
      }
    }

  }
}
