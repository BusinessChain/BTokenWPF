using System;
using System.Linq;


namespace BTokenLib
{
  internal abstract partial class Token
  {
    public class TXOutputTokenAnchor : TXOutput
    {
      public static byte[] IDENTIFIER_BTOKEN_PROTOCOL = new byte[] { (byte)'B', (byte)'T' };

      public const byte OP_RETURN = 0x6A;
      public const byte LengthDataAnchorToken = 70;

      public static byte[] PREFIX_ANCHOR_TOKEN =
        new byte[] { OP_RETURN, LengthDataAnchorToken }.Concat(TXOutputTokenAnchor.IDENTIFIER_BTOKEN_PROTOCOL).ToArray();

      public readonly static int LENGTH_SCRIPT_ANCHOR_TOKEN =
        PREFIX_ANCHOR_TOKEN.Length + LENGTH_IDTOKEN + 32 + 32;

      public const int LENGTH_IDTOKEN = 4;
      public byte[] IDToken = new byte[LENGTH_IDTOKEN];

      public byte[] HashBlockReferenced = new byte[32];
      public byte[] HashBlockPreviousReferenced = new byte[32];


      public TXOutputTokenAnchor()
      { }

      public TXOutputTokenAnchor(byte[] buffer, ref int startIndex)
      {
        startIndex += PREFIX_ANCHOR_TOKEN.Length;

        Array.Copy(buffer, startIndex, IDToken, 0, LENGTH_IDTOKEN);
        startIndex += LENGTH_IDTOKEN;

        Array.Copy(buffer, startIndex, HashBlockReferenced, 0, HashBlockReferenced.Length);
        startIndex += HashBlockReferenced.Length;

        Array.Copy(buffer, startIndex, HashBlockPreviousReferenced, 0, HashBlockPreviousReferenced.Length);
        startIndex += HashBlockPreviousReferenced.Length;
      }

      public TXOutputTokenAnchor Copy()
      {
        TXOutputTokenAnchor tokenAnchor = new();

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
