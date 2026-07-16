using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;


namespace BTokenLib
{
  public partial class TokenBitcoin : Token
  {
    public partial class WalletBitcoin : Wallet
    {
      public TokenBitcoin Token;

      //public const byte OP_RETURN = 0x6A;
      //public const byte LengthDataAnchorToken = 70;

      //public static byte[] PREFIX_ANCHOR_TOKEN =
      //  new byte[] { OP_RETURN, LengthDataAnchorToken }.Concat(TXOutputTokenAnchor.IDENTIFIER_BTOKEN_PROTOCOL).ToArray();

      //public readonly static int LENGTH_SCRIPT_ANCHOR_TOKEN =
      //  PREFIX_ANCHOR_TOKEN.Length + TXOutputTokenAnchor.LENGTH_IDTOKEN + 32 + 32;

      public List<TXOutputWallet> OutputsSpendable = new();

      // Das muss eine Datanbank sein!!
      public Dictionary<byte[], TX> IndexTXs = new(new EqualityComparerByteArray());


      public WalletBitcoin(string privKeyDec, TokenBitcoin token)
        : base(privKeyDec)
      {
        Token = token;
      }

      public override void ReverseBlock(Block block)
      {
        for (int t = block.TXs.Count - 1; t >= 0; t--)
        {
          TXBitcoin tX = block.TXs[t] as TXBitcoin;

          OutputsSpendable.RemoveAll(o => o.TXID.IsAllBytesEqual(tX.Hash));

          foreach (TXInputBitcoin tXInput in tX.Inputs)
          {
            TX tXReferenced = block.TXs.Find(t => t.Hash.IsAllBytesEqual(tXInput.TXIDOutput));

            if (tXReferenced != null || IndexTXs.TryGetValue(tXInput.TXIDOutput, out tXReferenced))
              TryAddTXOutputWallet(OutputsSpendable, tXReferenced as TXBitcoin, tXInput.OutputIndex);
          }

          IndexTXs.Remove(tX.Hash);
        }
      }
    }
  }
}