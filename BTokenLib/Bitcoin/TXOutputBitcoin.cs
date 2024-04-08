using System;
using System.Collections.Generic;
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

    public TXOutputBitcoin(byte[] buffer, ref int index)
    {
      Type = TypesToken.Unspecified;

      Value = BitConverter.ToInt64(buffer, index);
      index += 8;

      int lengthScript = VarInt.GetInt32(buffer, ref index);
      int indexEndOfScript = index + lengthScript;

      if (lengthScript == WalletBitcoin.LENGTH_SCRIPT_P2PKH &&
        WalletBitcoin.PREFIX_P2PKH.IsEqual(buffer, index))
      {
        index += WalletBitcoin.PREFIX_P2PKH.Length;

        Array.Copy(buffer, index, PublicKeyHash160, 0, PublicKeyHash160.Length);

        index += PublicKeyHash160.Length;

        if (WalletBitcoin.POSTFIX_P2PKH.IsEqual(buffer, index))
          Type = TypesToken.ValueTransfer;

        index += 2;
      }
      else if (lengthScript == WalletBitcoin.LENGTH_SCRIPT_ANCHOR_TOKEN &&
        WalletBitcoin.PREFIX_ANCHOR_TOKEN.IsEqual(buffer, index))
      {
        index += WalletBitcoin.PREFIX_ANCHOR_TOKEN.Length;

        TokenAnchor = new();

        Array.Copy(buffer, index, TokenAnchor.IDToken, 0, TokenAnchor.IDToken.Length);
        index += TokenAnchor.IDToken.Length;

        Array.Copy(
          buffer,
          index,
          TokenAnchor.HashBlockReferenced,
          0,
          TokenAnchor.HashBlockReferenced.Length);

        index += 32;

        Array.Copy(
          buffer,
          index,
          TokenAnchor.HashBlockPreviousReferenced,
          0,
          TokenAnchor.HashBlockPreviousReferenced.Length);

        index += TokenAnchor.HashBlockPreviousReferenced.Length;

        Type = TypesToken.AnchorToken;
      }
      else
        Type = TypesToken.Unspecified;

      if (index != indexEndOfScript)
      {
        Type = TypesToken.Unspecified;
        index = indexEndOfScript;
      }
    }
  }
}
