using System;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using System.IO;

namespace BTokenLib
{
  class BlockBToken : Block
  {
    public BlockBToken(Token token) : base(token)
    { }

  }
}

