using System;
using System.Linq;


namespace BTokenLib
{
  public partial class Token
  {
    public class TokenAnchor : TXOutput
    {
      public static byte[] IDENTIFIER_BTOKEN_PROTOCOL = new byte[] { (byte)'B', (byte)'T' };

      public const int LENGTH_IDTOKEN = 4;
      public byte[] IDToken = new byte[LENGTH_IDTOKEN];

      public byte[] HashBlockReferenced = new byte[32];
      public byte[] HashBlockPreviousReferenced = new byte[32];


      public TokenAnchor(byte[] buffer, ref int startIndex)
      {
        startIndex += WalletBitcoin.PREFIX_ANCHOR_TOKEN.Length;

        Array.Copy(buffer, startIndex, TokenAnchor.IDToken, 0, TokenAnchor.LENGTH_IDTOKEN);
        startIndex += TokenAnchor.LENGTH_IDTOKEN;

        Array.Copy(buffer, startIndex, TokenAnchor.HashBlockReferenced, 0, TokenAnchor.HashBlockReferenced.Length);
        startIndex += TokenAnchor.HashBlockReferenced.Length;

        Array.Copy(buffer, startIndex, TokenAnchor.HashBlockPreviousReferenced, 0, TokenAnchor.HashBlockPreviousReferenced.Length);
        startIndex += TokenAnchor.HashBlockPreviousReferenced.Length;

        Type = TypesToken.AnchorToken;
      }

      public TokenAnchor Copy()
      {
        TokenAnchor tokenAnchor = new();

        tokenAnchor.IDToken = IDToken;
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
    }
  }
}
