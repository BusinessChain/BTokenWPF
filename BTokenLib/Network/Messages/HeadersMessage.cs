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
        Header HeaderRoot;
        Header HeaderTip;

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



        //Not DoS save yet, when unsolicited zero headers or orphans are received.
        public override void Run(Peer peer)
        {
          int startIndex = 0;
          int countHeaders = VarInt.GetInt(Payload, ref startIndex);

          if (countHeaders > 2000)
            throw new ProtocolException($"Too many headers {countHeaders} in headers message.");
          else if(countHeaders > 0)
          {
            for (int i = 0; i < countHeaders; i += 1)
            {
              Header header = peer.Network.Token.ParseHeader(Payload, ref startIndex, SHA256);
              int countTXs = VarInt.GetInt(Payload, ref startIndex);

              if (HeaderRoot == null)
              {
                if(!peer.Network.TryConnectHeaderToChain(header))
                {
                  peer.SendGetHeaders(peer.Network.GetLocator());
                  return;
                }

                HeaderRoot = header;
                HeaderTip = header;
              }
              else
              {
                header.AppendToHeader(HeaderTip);
                HeaderTip.HeaderNext = header;
                HeaderTip = HeaderTip.HeaderNext;
              }
            }

            peer.SendGetHeaders(new List<Header> { HeaderTip });
          }
          else if (countHeaders == 0 && HeaderRoot != null)
          {
            double difficultyAccumulatedLocal = peer.Network.GetDifficultyAccumulatedHeaderTip();

            if (HeaderTip.DifficultyAccumulated > difficultyAccumulatedLocal)
              peer.ReceiveHeaderchain(HeaderRoot, HeaderTip);
            else if(HeaderTip.DifficultyAccumulated < difficultyAccumulatedLocal)
              peer.SendHeaders(); // sende hier alle headers so, damit sie bei ihm gerade die stärkere mainchain ergeben

            HeaderRoot = null;
            HeaderTip = null;
          }    
        }

      }
    }
  }
}