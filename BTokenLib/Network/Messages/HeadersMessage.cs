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
              Header header = Network.Token.ParseHeader(Payload, ref startIndex, SHA256);
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
            if (Network.SynchronizationRoot.TryInsertHeaderchain(HeaderRoot, out peer.Synchronization))
              peer.RequestBlock();
            else
            {
              // Ich nehme an, das passiert meistens wenn Sync schon gelockt ist oder bei Orphan
              // Hier Dos Counter machen.
            }

            HeaderRoot = null;
            HeaderTip = null;
          }
        }
      }
    }
  }
}