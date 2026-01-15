using System;

namespace BTokenLib
{
  partial class Network
  {
    class PongMessage : MessageNetwork
    {
      public UInt64 Nonce;


      public PongMessage()
        : base("pong")
      { }

      public PongMessage(byte[] payload) 
        : base("pong")
      {
        Payload = payload;
        LengthDataPayload = Payload.Length;
      }

      public override MessageNetwork Create()
      {
        return new PongMessage();
      }

      public override void RunMessage(Peer peer)
      {

      }
    }
  }
}
