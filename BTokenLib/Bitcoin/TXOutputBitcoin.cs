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
      ValueTransfer = 0x00,
      AnchorToken = 0x01,
      NotSupported
    }

    public TypesToken Type;

    public byte[] PublicKeyHash160 = new byte[20];
    public long Value;

    public TXOutputBitcoin(
      byte[] buffer,
      ref int index)
    {
      Value = BitConverter.ToInt64(buffer, index);
      index += 8;

      int lengthScript = VarInt.GetInt32(buffer, ref index);
      
      if (WalletBitcoin.PREFIX_P2PKH.IsEqual(buffer, index))
      {
        index += WalletBitcoin.PREFIX_P2PKH.Length;

        Array.Copy(buffer, index, PublicKeyHash160, 0, PublicKeyHash160.Length);

        index += PublicKeyHash160.Length;

        if (!WalletBitcoin.POSTFIX_P2PKH.IsEqual(buffer, index))
          Type = TypesToken.NotSupported;
        else
          Type = TypesToken.ValueTransfer;

        index += 2;
      }
      else if()
      {

      }

      index += lengthScript;
    }
  }
}
