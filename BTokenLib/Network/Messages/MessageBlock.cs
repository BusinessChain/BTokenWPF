using System;


namespace BTokenLib
{
  partial class Network
  {
    class MessageBlock : MessageNetwork
    { 
      public MessageBlock(byte[] bufferBlock, int lengthPayload)
        : base(
            "block",
            bufferBlock,
            0,
            lengthPayload)
      { }
    }
  }
}