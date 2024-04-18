using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Security.Cryptography;


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