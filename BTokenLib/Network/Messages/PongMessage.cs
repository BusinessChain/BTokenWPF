using System;

namespace BTokenLib
{
  partial class Network
  {
    class PongMessage : MessageNetwork
    {
      public UInt64 Nonce;


      public PongMessage(byte[] payload) 
        : base("pong")
      {
        Payload = payload;
        LengthDataPayload = Payload.Length;
      }
    }
  }
}
