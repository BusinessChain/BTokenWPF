using System;
using System.Linq;
using System.Collections.Generic;


namespace BTokenLib
{
  partial class Network
  {
    partial class Peer
    {
      class GetHeadersMessage : MessageNetwork
      {
        public GetHeadersMessage()
          : base("getheaders") { }

        public GetHeadersMessage(List<Header> headerLocator, uint versionProtocol)
          : base("getheaders")
        {
          List<byte> payload = new();

          payload.AddRange(BitConverter.GetBytes(versionProtocol));
          payload.AddRange(VarInt.GetBytes(headerLocator.Count()));

          for (int i = 0; i < headerLocator.Count(); i++)
            payload.AddRange(headerLocator.ElementAt(i).Hash);

          payload.AddRange("0000000000000000000000000000000000000000000000000000000000000000".ToBinary());

          Payload = payload.ToArray();
          LengthDataPayload = Payload.Length;
        }



        public override MessageNetwork Create()
        {
          return new GetHeadersMessage();
        }

        public override void RunMessage(Peer peer)
        {

        }
      }
    }
  }
}