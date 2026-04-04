using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Security.Cryptography;


namespace BTokenLib
{
  partial class Network
  {
    partial class Peer
    {
      class HeadersMessage : MessageNetworkProtocol
      {
        Network Network;

        Block BlockDownload;

        SHA256 SHA256 = SHA256.Create();


        public HeadersMessage()
          : this(null)
        { }

        public HeadersMessage(Header headerRoot)
          : base("headers")
        {
          HeaderRoot = headerRoot;

          if (HeaderRoot != null)
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


        public override void Run(Peer peer)
        {
          IncrementDOSCounter();

          int startIndex = 0;
          int countHeaders = VarInt.GetInt(Payload, ref startIndex);

          if (countHeaders > 2000)
            throw new ProtocolException($"Too many headers {countHeaders} in headers message.");
          else if(countHeaders > 0)
          {
            Header headerRoot = ParseHeaderchain(countHeaders, ref startIndex);

            if (Network.SynchronizationRoot.TryExtendHeaderchain(
              headerRoot,
              out List<byte[]> headerslocator,
              ref BlockDownload))
            {
              DecrementDOSCounter();
            }

            peer.SendGetHeaders(headerslocator);
          }
          else if (countHeaders == 0)
          {
            BlockMessage blockMessage = peer.MessagesNetworkProtocol["block"] as BlockMessage;
            blockMessage.HeaderDownload = BlockDownload.Header;
            blockMessage.BlockDownload = BlockDownload;

            peer.SendBlockRequest(BlockDownload.Header.Hash);
          }
        }

        Header ParseHeaderchain(int countHeaders, ref int startIndex)
        {
          Header headerRoot = Network.Token.ParseHeader(Payload, ref startIndex, SHA256);
          VarInt.GetInt(Payload, ref startIndex);

          Header headerTip = headerRoot;

          countHeaders -= 1;

          while (countHeaders > 0)
          {
            Header header = Network.Token.ParseHeader(Payload, ref startIndex, SHA256);
            VarInt.GetInt(Payload, ref startIndex);

            header.AppendToHeader(headerTip);
            headerTip.HeaderNext = header;
            headerTip = header;

            countHeaders -= 1;
          }

          return headerRoot;
        }
      }
    }
  }
}