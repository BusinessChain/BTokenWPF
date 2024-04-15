using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;


namespace BTokenLib
{
  public class TokenAnchor
  {
    public byte[] IDToken = new byte[2];
    public int NumberSequence;

    public byte[] HashBlockReferenced = new byte[32];
    public byte[] HashBlockPreviousReferenced = new byte[32];

    public TX TX;

    public TokenAnchor Copy()
    {
      TokenAnchor tokenAnchor = new();

      tokenAnchor.IDToken = IDToken;
      tokenAnchor.NumberSequence = NumberSequence;
      tokenAnchor.HashBlockReferenced = HashBlockReferenced;
      tokenAnchor.HashBlockPreviousReferenced = HashBlockPreviousReferenced;

      return tokenAnchor;
    }
  }
}
