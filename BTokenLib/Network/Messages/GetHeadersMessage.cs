using System;
using System.Linq;
using System.Collections.Generic;


namespace BTokenLib
{
  partial class Network
  {
    class GetHeadersMessage : MessageNetworkProtocol
    {
      public GetHeadersMessage(Network network)
        : base("getheaders", network) { }

      public GetHeadersMessage(List<byte[]> headerLocator, uint versionProtocol)
        : base("getheaders")
      {
        List<byte> payload = new();

        payload.AddRange(BitConverter.GetBytes(versionProtocol));
        payload.AddRange(VarInt.GetBytes(headerLocator.Count()));

        for (int i = 0; i < headerLocator.Count(); i++)
          payload.AddRange(headerLocator.ElementAt(i));

        payload.AddRange("0000000000000000000000000000000000000000000000000000000000000000".ToBinary());

        Payload = payload.ToArray();
        LengthDataPayload = Payload.Length;
      }

      public override void RunMessage(Peer peer)
      {

      }
    }
  }
}