using System;


namespace BTokenLib
{
  partial class Network
  {
    class TXMessage : MessageNetwork
    {
      public TXMessage(byte[] tXRaw) 
        : base("tx")
      {
        Payload = tXRaw;
        LengthDataPayload = Payload.Length;
      }
    }
  }
}
