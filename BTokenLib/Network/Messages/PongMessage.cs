using System;

namespace BTokenLib
{
  partial class Network
  {
    partial class Peer
    {
      class PongMessage : MessageNetworkProtocol
      {
        public const string Command = "pong";


        public PongMessage()
        { }

        public PongMessage(byte[] payload, int lengthDataPayload)
        {
          Payload = payload;
          LengthDataPayload = lengthDataPayload;
        }

        public override void Run(Peer peer)
        {
          PingMessage messagePing = peer.MessagesNetworkProtocol[PingMessage.Command];

          if (messagePing == null)
            throw new ProtocolException("Transistion into state 'pong' from other than state 'ping' is not supported.");

          if (messagePing.Payload != Payload)
            throw new ProtocolException("'Pong' message did not return same nonce as sended in 'ping' message.");

          peer.MessageNetworkCurrent = null;
        }

        public override string GetCommand()
        {
          return Command;
        }
      }
    }
  }
}