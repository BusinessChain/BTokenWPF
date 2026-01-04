using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;


namespace BTokenLib
{
  public partial class TokenBitcoin : Token
  {
    class EqualityComparerTXOutputWallet : IEqualityComparer<TXOutputWallet>
    {
      public bool Equals(TXOutputWallet x, TXOutputWallet y)
      {
        return x.Index == y.Index && x.TXID.IsAllBytesEqual(y.TXID);
      }

      public int GetHashCode(TXOutputWallet x)
      {
        return BitConverter.ToInt32(x.TXID, 0) + x.Index;
      }
    }

    public partial class WalletBitcoin : Wallet
    {
      public TokenBitcoin Token;

      public byte[] PublicScript;

      public const byte OP_RETURN = 0x6A;
      public const byte LengthDataAnchorToken = 70;

      public static byte[] PREFIX_ANCHOR_TOKEN =
        new byte[] { OP_RETURN, LengthDataAnchorToken }.Concat(TokenAnchor.IDENTIFIER_BTOKEN_PROTOCOL).ToArray();

      public readonly static int LENGTH_SCRIPT_ANCHOR_TOKEN =
        PREFIX_ANCHOR_TOKEN.Length + TokenAnchor.LENGTH_IDTOKEN + 32 + 32;

      public List<TXOutputWallet> OutputsSpendable = new();

      // Das muss eine Datanbank sein!!
      public Dictionary<byte[], TX> IndexTXs = new(new EqualityComparerByteArray());


      public WalletBitcoin(string privKeyDec, TokenBitcoin token)
        : base(privKeyDec)
      {
        Token = token;

        PublicScript = PREFIX_P2PKH.Concat(Hash160PKeyPublic).Concat(POSTFIX_P2PKH).ToArray();
      }


      List<(TX tX, int ageBlock)> TXsUnconfirmedCreated = new();


      public override void SendTXValue(
        string addressOutput,
        long valueOutput,
        double feePerByte,
        int sequence)
      {
        TXOutputBitcoin tXOutput = new()
        {
          Type = TXOutputBitcoin.TypesToken.P2PKH,
          Value = valueOutput,
          Script = PREFIX_P2PKH.Concat(addressOutput.Base58CheckToPubKeyHash()).Concat(POSTFIX_P2PKH).ToArray()
        };

        SendTX(tXOutput, feePerByte, sequence);
      }

      public override void SendTXData(byte[] data, double feePerByte, int sequence)
      {
        TXOutputBitcoin tXOutput = new()
        {
          Type = TXOutputBitcoin.TypesToken.Data,
          Value = 0,
          Script = new List<byte>() { OP_RETURN, (byte)data.Length }.Concat(data).ToArray()
        };

        SendTX(tXOutput, feePerByte, sequence);
      }

      void SendTX(TXOutputBitcoin tXOutput, double feePerByte, int sequence)
      {
        //return new()
        //{
        //  new TXOutputWallet()
        //  {
        //    TXID = "20da7491ec53757a914dc1f045afbcb0a5c3396785a9abe9fc074e017e9403fd".ToBinary(),
        //    Value = 7106,
        //    Index = 1
        //  }
        //};

        long feePerInputP2PKH = (long)(LENGTH_P2PKH_INPUT * feePerByte);
        long feePerOutputP2PKH = (long)(LENGTH_P2PKH_OUTPUT * feePerByte);

        List<TXOutputWallet> outputsSpendable = OutputsSpendable
          .Where(o => o.Value > feePerInputP2PKH)
          .Concat(OutputsSpendableUnconfirmed.Where(o => o.Value > feePerInputP2PKH))
          .Except(OutputsSpentUnconfirmed, new EqualityComparerTXOutputWallet())
          .Take(VarInt.PREFIX_UINT16 - 1).ToList();

        long valueInputs = OutputsSpendable.Sum(o => o.Value);

        long feeTX = (long)(feePerByte
          * (LENGTH_P2PKH_INPUT * OutputsSpendable.Count
          + LENGTH_TX_OVERHEAD
          + tXOutput.Script.Length));

        if (valueInputs < feeTX + tXOutput.Value)
          throw new ProtocolException(
            $"Not enough funds held in unspent outputs: {valueInputs} sats." +
            $"Fee required by P2PKH transaction: {feeTX}. Reduce specified rate for fee per byte.");

        long valueChange = valueInputs - tXOutput.Value - feeTX - feePerOutputP2PKH;

        // The premis is that the value of the change output has to be greater than the fee of one input,
        // so that a future spend of that output is economically feasible.
        bool flagCreateOutputChange = valueChange > feePerInputP2PKH;

        TXBitcoin tX = new();

        foreach (TXOutputWallet outputSpendable in OutputsSpendable)
        {
          tX.Inputs.Add(new TXInputBitcoin
          {
            TXIDOutput = outputSpendable.TXID,
            OutputIndex = outputSpendable.Index,
            Sequence = sequence
          });
        }

        tX.TXOutputs.Add(tXOutput);

        if (flagCreateOutputChange)
          tX.TXOutputs.Add(new TXOutputBitcoin
          {
            Type = TXOutputBitcoin.TypesToken.P2PKH,
            Value = valueChange,
            Script = PREFIX_P2PKH.Concat(Hash160PKeyPublic).Concat(POSTFIX_P2PKH).ToArray()
          });

        tX.Serialize(this);

        TXsUnconfirmedCreated.Add((tX, 0));

        Token.BroadcastTX(tX);
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
        TXOutputBitcoin tXOutputReferenced = tX.TXOutputs[indexOutput];

        if (tXOutputReferenced.Type == TXOutputBitcoin.TypesToken.P2PKH &&
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