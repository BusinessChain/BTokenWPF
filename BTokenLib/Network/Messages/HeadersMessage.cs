using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
        public List<Header> Headers = new();
        HeaderchainDownload HeaderchainDownload = new();

        public SHA256 SHA256 = SHA256.Create();

        public HeadersMessage()
          : this(new List<Header>()) 
        { }

        public HeadersMessage(List<Header> headers)
          : base("headers")
        {
          Headers = headers;

          if(Headers.Any())
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
          ParsePayload(peer.Network.Token);

          if (HeaderchainDownload.Locator == null)
            HeaderchainDownload.LoadLocator(peer.Network.GetLocator());

          if (Headers.Count == 0 && HeaderchainDownload.IsStrongerThanHeaderTipLocator())
          {
            if (HeaderchainDownload.IsFork)
              peer.Network.TryReverseBlockchainToHeight(HeaderchainDownload.GetHeightAncestor());

            peer.Network.StartSynchronizationBlocks(HeaderchainDownload); // when sync is finished set HeaderchainDownload.Locator = null
          }
          else if (Headers.Any())
          {
            HeaderchainDownload.TryInsertHeaders(Headers);
            peer.SendGetHeaders(HeaderchainDownload.Locator);
          }
        }

        void ParsePayload(Token token)
        {
          int startIndex = 0;
          int countHeaders = VarInt.GetInt(Payload, ref startIndex);

          for (int i = 0; i < countHeaders; i += 1)
          {
            Header header = token.ParseHeader(Payload, ref startIndex, SHA256);
            Headers.Add(header);
            startIndex += 1; // Number of transaction in the block, which in the header is always 0
          }
        }
      }
    }
  }
}