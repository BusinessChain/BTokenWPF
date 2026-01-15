using System;


namespace BTokenLib
{
  partial class Network
  {
    class TXMessage : MessageNetwork
    {
      public TXMessage()
        : base("tx") { }

      public TXMessage(byte[] tXRaw) 
        : base("tx")
      {
        Payload = tXRaw;
        LengthDataPayload = Payload.Length;
      }

      public override MessageNetwork Create()
      {
        return new TXMessage();
      }

      public override void RunMessage(Peer peer)
      {

      }
    }
  }
}
