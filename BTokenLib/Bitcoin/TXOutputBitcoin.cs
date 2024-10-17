using System;
using System.IO;


namespace BTokenLib
{
  public class TXOutputBitcoin
  {
    public enum TypesToken
    {
      Unspecified = 0x00,
      P2PKH = 0x01,
      AnchorToken = 0x02,
      Data = 0x03
    }

    public TypesToken Type;

    public byte[] PublicKeyHash160 = new byte[20];
    public long Value;

    public byte[] Data;
    public TokenAnchor TokenAnchor;


    public TXOutputBitcoin(byte[] buffer, ref int startIndex)
    {
      Type = TypesToken.Unspecified;

      Value = BitConverter.ToInt64(buffer, startIndex);
      startIndex += 8;

      int lengthScript = VarInt.GetInt(buffer, ref startIndex);

      if (lengthScript == WalletBitcoin.LENGTH_SCRIPT_P2PKH &&
        WalletBitcoin.PREFIX_P2PKH.IsAllBytesEqual(buffer, startIndex))
      {
        startIndex += WalletBitcoin.PREFIX_P2PKH.Length;

        Array.Copy(buffer, startIndex, PublicKeyHash160, 0, PublicKeyHash160.Length);
        startIndex += PublicKeyHash160.Length;

        if (WalletBitcoin.POSTFIX_P2PKH.IsAllBytesEqual(buffer, startIndex))
        {
          startIndex += WalletBitcoin.POSTFIX_P2PKH.Length;
          Type = TypesToken.P2PKH;
        }
      }
      else if (lengthScript == WalletBitcoin.LENGTH_SCRIPT_ANCHOR_TOKEN &&
        WalletBitcoin.PREFIX_ANCHOR_TOKEN.IsAllBytesEqual(buffer, startIndex))
      {
        startIndex += WalletBitcoin.PREFIX_ANCHOR_TOKEN.Length;

        TokenAnchor = new();

        Array.Copy(buffer, startIndex, TokenAnchor.IDToken, 0, TokenAnchor.LENGTH_IDTOKEN);
        startIndex += TokenAnchor.LENGTH_IDTOKEN;

        Array.Copy(buffer, startIndex, TokenAnchor.HashBlockReferenced, 0, TokenAnchor.HashBlockReferenced.Length);
        startIndex += TokenAnchor.HashBlockReferenced.Length;

        Array.Copy(buffer, startIndex, TokenAnchor.HashBlockPreviousReferenced, 0, TokenAnchor.HashBlockPreviousReferenced.Length);
        startIndex += TokenAnchor.HashBlockPreviousReferenced.Length;

        Type = TypesToken.AnchorToken;
      }
      else if (lengthScript == WalletBitcoin.LENGTH_SCRIPT_ANCHOR_TOKEN &&
        WalletBitcoin.PREFIX_ANCHOR_TOKEN.IsAllBytesEqual(buffer, startIndex))
      {

      }
      else
        Type = TypesToken.Unspecified;
    }
  }
}
