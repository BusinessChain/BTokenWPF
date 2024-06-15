using System;
using System.IO;
using System.Security.Cryptography;


namespace BTokenLib
{
  class BlockBitcoin : Block
  {
    public BlockBitcoin(Token token) 
      : base(token)
    { }

  }
}
