using System;

namespace BTokenLib
{
  partial class Network
  {
    partial class Peer
    {
      class PongMessage : MessageNetwork
      {
        public PongMessage()
          : base("pong")
        { }

        public PongMessage(byte[] payload, int lengthDataPayload)
          : base("pong")
        {
          Payload = payload;
          LengthDataPayload = lengthDataPayload;
        }

        public override MessageNetwork Create()
        {
          return new PongMessage();
        }

        public override void RunMessage(Peer peer)
        {
          PingMessage messagePing = messageNetworkOld as PingMessage;

          if (messagePing == null)
            throw new ProtocolException("Transistion into state 'pong' from other than state 'ping' is not supported.");

          if (messagePing.Payload != Payload)
            throw new ProtocolException("'Pong' message did not return same nonce as sended in 'ping' message.");

          peer.MessageNetworkCurrent = null;
        }
      }
    }
  }
}