using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;


namespace BTokenLib
{
  public class TXOutputBitcoin
  {
    public enum TypesToken
    {
      Unspecified = 0x00,
      ValueTransfer = 0x01,
      AnchorToken = 0x02,
    }

    public TypesToken Type;

    public byte[] PublicKeyHash160 = new byte[20];
    public long Value;

    public TokenAnchor TokenAnchor;


    public TXOutputBitcoin(Stream stream)
    {
      Type = TypesToken.Unspecified;

      Value = stream.ReadInt64();

      int lengthScript = VarInt.GetInt(stream);

      if (lengthScript == WalletBitcoin.LENGTH_SCRIPT_P2PKH &&
        stream.IsEqual(WalletBitcoin.PREFIX_P2PKH))
      {
        stream.Read(PublicKeyHash160, 0, PublicKeyHash160.Length);

        if (stream.IsEqual(WalletBitcoin.POSTFIX_P2PKH))
          Type = TypesToken.ValueTransfer;
      }
      else if (lengthScript == WalletBitcoin.LENGTH_SCRIPT_ANCHOR_TOKEN &&
        stream.IsEqual(WalletBitcoin.PREFIX_ANCHOR_TOKEN))
      {
        TokenAnchor = new();

        stream.Read(TokenAnchor.IDToken, 0, TokenAnchor.LENGTH_IDTOKEN);

        stream.Read(
          TokenAnchor.HashBlockReferenced, 
          0, 
          TokenAnchor.HashBlockReferenced.Length);

        stream.Read(
          TokenAnchor.HashBlockPreviousReferenced,
          0,
          TokenAnchor.HashBlockPreviousReferenced.Length);

        Type = TypesToken.AnchorToken;
      }
      else
        Type = TypesToken.Unspecified;
    }
  }
}
