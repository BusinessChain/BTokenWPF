using System;


namespace BTokenLib
{
  partial class Network
  {
    class MessageBlock : MessageNetwork
    { 
      public MessageBlock(byte[] bufferBlock)
        : base(
            "block",
            bufferBlock,
            0,
            bufferBlock.Length)
      { }
    }
  }
}