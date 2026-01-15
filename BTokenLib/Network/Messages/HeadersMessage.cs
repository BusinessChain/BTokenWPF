using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;

namespace BTokenLib
{
  partial class Network
  {
    class HeadersMessage : MessageNetwork
    {
      public List<Header> Headers = new();

      public HeadersMessage()
        : base("headers") { }

      public HeadersMessage(List<Header> headers)
        : base("headers")
      {
        Headers = headers;

        List<byte> payload = new();

        payload.AddRange(VarInt.GetBytes(Headers.Count));

        foreach (Header header in Headers)
        {
          payload.AddRange(header.Serialize());
          payload.Add(0);
        }

        Payload = payload.ToArray();
        LengthDataPayload = Payload.Length;
      }


      public override MessageNetwork Create()
      {
        return new HeadersMessage();
      }

      public override void RunMessage(Peer peer)
      {

      }
    }
  }
}