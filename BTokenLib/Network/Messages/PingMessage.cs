using System;
using System.Collections.Generic;

namespace BTokenLib
{
  partial class Network
  {
    partial class Peer
    {
      class PingMessage : MessageNetwork
      {
        public UInt64 Nonce;


        public PingMessage()
          : base("ping")
        { }

        public PingMessage(byte[] payload)
          : base("ping")
        {
          Payload = payload;
          LengthDataPayload = Payload.Length;
        }

        public override MessageNetwork Create()
        {
          return new PingMessage();
        }

        public override void RunMessage(Peer peer)
        {
          $"Received ping message.".Log(this, peer.LogEntryNotifier);

          peer.SendMessage(new PongMessage(Payload, LengthDataPayload));
        }
      }
    }
  }
}
