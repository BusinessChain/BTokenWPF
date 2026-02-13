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
        List<Header> Headers = new();

        SHA256 SHA256 = SHA256.Create();


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


        void ParsePayload(Token token)
        {
          int startIndex = 0;
          int countHeaders = VarInt.GetInt(Payload, ref startIndex);

          if (countHeaders > 2000)
            throw new ProtocolException($"Too many headers {countHeaders} in headers message.");

          for (int i = 0; i < countHeaders; i += 1)
          {
            Header header = token.ParseHeader(Payload, ref startIndex, SHA256);
            Headers.Add(header);
            startIndex += 1; // Number of transaction in the block, which in the standalone header is always 0
          }
        }

        public override void Run(Peer peer)
        {
          ParsePayload(peer.Network.Token);

          peer.ReceiveHeadersMessage(Headers);          
        }

      }
    }
  }
}