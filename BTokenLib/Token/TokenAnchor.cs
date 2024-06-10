using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;


namespace BTokenLib
{
  public class TokenAnchor
  {
    public static byte[] IDENTIFIER_BTOKEN_PROTOCOL = new byte[] { (byte)'B', (byte)'T' };

    public const int LENGTH_IDTOKEN = 4;
    public byte[] IDToken = new byte[LENGTH_IDTOKEN];

    public int NumberSequence = 0;
    public double FeeSatoshiPerByte;

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

    public byte[] Serialize()
    {
      return IDENTIFIER_BTOKEN_PROTOCOL
      .Concat(IDToken)
      .Concat(HashBlockReferenced)
      .Concat(HashBlockPreviousReferenced).ToArray();
    }

    public override string ToString()
    {
      return TX.ToString();
    }
  }
}
