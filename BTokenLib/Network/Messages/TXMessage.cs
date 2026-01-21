using System;


namespace BTokenLib
{
  partial class Network
  {
    partial class Peer
    {
      class TXMessage : MessageNetworkProtocol
      {
        public TXMessage()
          : base("tx") { }

        public TXMessage(byte[] tXRaw)
          : base("tx")
        {
          Payload = tXRaw;
          LengthDataPayload = Payload.Length;
        }

        public override MessageNetworkProtocol Create()
        {
          return new TXMessage();
        }

        public override void RunMessage(Peer peer)
        {

        }
      }
    }
  }
}
