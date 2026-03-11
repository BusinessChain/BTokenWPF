using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;

namespace BTokenLib
{
  partial class Network
  {
    partial class Peer
    {
      class HeadersMessage : MessageNetworkProtocol
      {
        Network Network;
        Header HeaderRoot;
        Header HeaderTip;

        SHA256 SHA256 = SHA256.Create();


        public HeadersMessage()
          : this(null) 
        { }

        public HeadersMessage(Header headerRoot)
          : base("headers")
        {
          HeaderRoot = headerRoot;

          if(HeaderRoot != null)
          {
            int indexPayload = 0;

            byte[] headersCountVarIntFormat = VarInt.GetBytes(Headers.Count);
            Array.Copy(headersCountVarIntFormat, 0, Payload, indexPayload, headersCountVarIntFormat.Length);

            indexPayload += headersCountVarIntFormat.Length;

            foreach (Header header in Headers)
            {
              byte[] headerSerialized = header.Serialize();
              Array.Copy(headerSerialized, 0, Payload, indexPayload, headerSerialized.Length);
              indexPayload += headerSerialized.Length;

              Payload[indexPayload] = 0;
              indexPayload += 1;
            }
            LengthDataPayload = indexPayload;
          }
        }


        Header ParseHeader(ref int index)
        {
          byte[] hash = SHA256.ComputeHash(
          SHA256.ComputeHash(
            Payload,
            index,
            HeaderBitcoin.COUNT_HEADER_BYTES));

          uint version = BitConverter.ToUInt32(Payload, index);
          index += 4;

          byte[] previousHeaderHash = new byte[32];
          Array.Copy(Payload, index, previousHeaderHash, 0, 32);
          index += 32;

          byte[] merkleRootHash = new byte[32];
          Array.Copy(Payload, index, merkleRootHash, 0, 32);
          index += 32;

          uint unixTimeSeconds = BitConverter.ToUInt32(Payload, index);
          index += 4;

          bool isBlockTimePremature = unixTimeSeconds >
            (DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 2 * 60 * 60);

          if (isBlockTimePremature)
            throw new ProtocolException($"Timestamp premature {new DateTime(unixTimeSeconds).Date}.");

          uint nBits = BitConverter.ToUInt32(Payload, index);
          index += 4;

          if (hash.IsGreaterThan(nBits))
            throw new ProtocolException($"Header hash {hash.ToHexString()} greater than NBits {nBits}.");

          uint nonce = BitConverter.ToUInt32(Payload, index);
          index += 4;

          return new Header(
            hash,
            version,
            previousHeaderHash,
            merkleRootHash,
            unixTimeSeconds,
            nBits,
            nonce);
        }

        //Not DoS save yet, when unsolicited zero headers or orphans are received.
        public override void Run(Peer peer)
        {
          int startIndex = 0;
          int countHeaders = VarInt.GetInt(Payload, ref startIndex);

          if (countHeaders > 2000)
            throw new ProtocolException($"Too many headers {countHeaders} in headers message.");
          else if (countHeaders > 0)
          {
            Header headerRoot = null;
            Header headerTip = null;

            for (int i = 0; i < countHeaders; i += 1)
            {
              Header header = ParseHeader(ref startIndex);
              int countTXs = VarInt.GetInt(Payload, ref startIndex);

              if (headerRoot == null)
              {
                headerRoot = header;
                headerTip = header;
              }
              else
              {
                header.AppendToHeader(headerTip);
                headerTip.HeaderNext = header;
                headerTip = header;
              }
            }

            if (HeaderRoot == null)
            {
              if (!peer.Network.TryConnectHeaderToChain(ref headerRoot))
              {
                // Hier Dos Counter machen, damit bei tiefer Fork kein deadlock.
                peer.SendGetHeaders(peer.Network.GetLocator());
                return;
              }

              HeaderRoot = headerRoot;
              HeaderTip = headerTip;
            }
            else
            {
              headerRoot.AppendToHeader(HeaderTip);
              HeaderTip.HeaderNext = headerRoot;
              HeaderTip = headerTip;
            }

            peer.SendGetHeaders(new List<Header> { HeaderTip });
          }
          else if (countHeaders == 0 && HeaderRoot != null)
          {
            peer.Synchronization = new Synchronization(HeaderRoot, HeaderTip);

            if (Network.TryInsertSynchronization(ref peer.Synchronization))
              peer.RequestBlock();
            else
              peer.Synchronization.FlagIsAborted = true;
          }
        }
      }
    }
  }
}