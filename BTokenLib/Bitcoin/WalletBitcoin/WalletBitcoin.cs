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

      public override void SendTXValue(
        string addressOutput,
        long value,
        double feePerByte,
        int sequence)
      {
        TXOutputBitcoin tXOutput = new()
        {
          Type = TXOutput.TypesToken.P2PKH,
          Value = value,
          Script = PREFIX_P2PKH.Concat(addressOutput.Base58CheckToPubKeyHash()).Concat(POSTFIX_P2PKH).ToArray()
        };

        SendTX(tXOutput, feePerByte, sequence);
      }

      public override void InsertTXUnconfirmed(TX tX)
      {
        TXBitcoin tXBitcoin = tX as TXBitcoin;

        foreach (TXInputBitcoin tXInput in tXBitcoin.Inputs)
        {
          TXOutputWallet tXOutputWallet = OutputsSpendable.Find(
            o => o.TXID.IsAllBytesEqual(tXInput.TXIDOutput) 
            && o.Index == tXInput.OutputIndex);

          if (tXOutputWallet != null)
            OutputsSpentUnconfirmed.Add(tXOutputWallet);
        }

        for (int i = 0; i < tXBitcoin.TXOutputs.Count; i++)
          TryAddTXOutputWallet(OutputsSpendableUnconfirmed, tXBitcoin, i);
      }

      public override void InsertBlock(Block block)
      {
        foreach (TXBitcoin tX in block.TXs)
        {
          foreach (TXInputBitcoin tXInput in tX.Inputs)
            if (TryRemoveOutput(OutputsSpendable, tXInput.TXIDOutput, tXInput.OutputIndex))
              TryRemoveOutput(OutputsSpentUnconfirmed, tXInput.TXIDOutput, tXInput.OutputIndex);

          for (int i = 0; i < tX.TXOutputs.Count; i++)
            if (TryAddTXOutputWallet(OutputsSpendable, tX, i))
              TryRemoveOutput(OutputsSpendableUnconfirmed, tX.Hash, i);

          IndexTXs.Add(tX.Hash, tX);
        }
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

      bool TryAddTXOutputWallet(List<TXOutputWallet> listOutputs, TXBitcoin tX, int indexOutput)
      {
        TXOutputBitcoin tXOutputReferenced = (TXOutputBitcoin)tX.TXOutputs[indexOutput];

        if (tXOutputReferenced.Type == TXOutput.TypesToken.P2PKH &&
          tXOutputReferenced.PublicKeyHash160.IsAllBytesEqual(Hash160PKeyPublic))
        {
          listOutputs.Add(
            new TXOutputWallet
            {
              TXID = tX.Hash,
              Index = indexOutput,
              Value = tXOutputReferenced.Value
            });

          return true;
        }

        return false;
      }

      static bool TryRemoveOutput(List<TXOutputWallet> outputs, byte[] tXID, int outputIndex)
      {
        return 0 < outputs.RemoveAll(o => o.TXID.IsAllBytesEqual(tXID) && o.Index == outputIndex);
      }

      public override void Clear()
      {
        OutputsSpendable.Clear();
        base.Clear();
      }

      public override long GetBalance()
      {
        return OutputsSpendable.Sum(o => o.Value);
      }
    }
  }
}