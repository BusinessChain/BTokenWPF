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


      public const byte LENGTH_SCRIPT_P2PKH = 25;
      public static byte[] PREFIX_P2PKH = new byte[] { 0x76, 0xA9, 0x14 };
      public static byte[] POSTFIX_P2PKH = new byte[] { 0x88, 0xAC };

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

      public override void SendTXValue(string addressOutput, long valueOutput, double feePerByte)
      {
        List<TXOutputWallet> outputsSpendable = OutputsSpendable
          .Where(o => o.Value > feePerTXInput)
          .Concat(OutputsSpendableUnconfirmed.Where(o => o.Value > feePerTXInput))
          .Except(OutputsSpentUnconfirmed, new EqualityComparerTXOutputWallet())
          .Take(VarInt.PREFIX_UINT16 - 1).ToList();

        //List<TXOutputWallet> outputsSpendable = new()
        //{
        //  new TXOutputWallet()
        //  {
        //    TXID = "20da7491ec53757a914dc1f045afbcb0a5c3396785a9abe9fc074e017e9403fd".ToBinary(),
        //    Value = 7106,
        //    Index = 1
        //  }
        //};


        BuilderTXBitcoinValue builderTXBitcoin =
          new(KeyPublic, addressOutput, outputsSpendable, valueOutput, feePerByte);

        // Besser wäre eigentlich, das TX Objekt direkt selber zu erstellen
        // und dann die serialisierte TXRaw zu versenden.

        TX tX = Token.ParseTX(builderTXBitcoin.TXRaw, SHA256);

        TXsUnconfirmedCreated.Add((tX, 0));

        Token.BroadcastTX(tX);
      }

      public override void SendTXData(byte[] data, double feePerByte)
      {
        if (!TryCreateTXInputScaffold(
          sequence,
          (long)(data.Length * feePerByte),
          feePerByte,
          out long valueInput,
          out long feeTXInputScaffold,
          out List<byte> tXRaw))
        {
          tX = null;
          return false;
        }

        long feeTX = feeTXInputScaffold
          + (long)(LENGTH_P2PKH_OUTPUT * feePerByte)
          + (long)(data.Length * feePerByte);

        long valueChange = valueInput - feeTX;

        if (valueChange > 0)
          tXRaw.Add(0x02);
        else
          tXRaw.Add(0x01);

        tXRaw.AddRange(BitConverter.GetBytes((long)0));
        tXRaw.Add((byte)(data.Length + 2));
        tXRaw.Add(OP_RETURN);
        tXRaw.Add((byte)data.Length);
        tXRaw.AddRange(data);

        if (valueChange > 0)
        {
          tXRaw.AddRange(BitConverter.GetBytes(valueChange));
          tXRaw.Add((byte)PublicScript.Length);
          tXRaw.AddRange(PublicScript);
        }

        tXRaw.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x00 }); // locktime
        tXRaw.AddRange(new byte[] { 0x01, 0x00, 0x00, 0x00 }); // sighash

        SignTX(tXRaw);

        tX = Token.ParseTX(tXRaw.ToArray(), SHA256);
        return true;
      }

      public void SignTX(List<byte> tXRaw)
      {
        List<List<byte>> signaturesPerInput = new();
        int countInputs = tXRaw[4];
        int indexFirstInput = 5;

        for (int i = 0; i < countInputs; i++)
        {
          List<byte> tXRawSign = tXRaw.ToList();
          int indexRawSign = indexFirstInput + 36 * (i + 1) + 5 * i;

          tXRawSign[indexRawSign++] = (byte)PublicScript.Length;
          tXRawSign.InsertRange(indexRawSign, PublicScript);

          byte[] message = SHA256.ComputeHash(tXRawSign.ToArray());

          byte[] signature = Crypto.GetSignature(KeyPrivateDecimal, message);

          List<byte> scriptSig = new();

          scriptSig.Add((byte)(signature.Length + 1));
          scriptSig.AddRange(signature);
          scriptSig.Add(0x01);

          scriptSig.Add((byte)KeyPublic.Length);
          scriptSig.AddRange(KeyPublic);

          signaturesPerInput.Add(scriptSig);
        }

        for (int i = countInputs - 1; i >= 0; i -= 1)
        {
          int indexSig = indexFirstInput + 36 * (i + 1) + 5 * i;

          tXRaw[indexSig++] = (byte)signaturesPerInput[i].Count;

          tXRaw.InsertRange(
            indexSig,
            signaturesPerInput[i]);
        }

        tXRaw.RemoveRange(tXRaw.Count - 4, 4);
      }

      bool TryCreateTXInputScaffold(
        int sequence,
        long valueNettoMinimum,
        double feePerByte,
        out long value,
        out long feeTXInputScaffold,
        out List<byte> tXRaw)
      {
        tXRaw = new();
        long feePerTXInput = (long)(feePerByte * LENGTH_P2PKH_INPUT);

        //List<TXOutputWallet> outputsSpendable = new()
        //{
        //  new TXOutputWallet()
        //  {
        //    TXID = "20da7491ec53757a914dc1f045afbcb0a5c3396785a9abe9fc074e017e9403fd".ToBinary(),
        //    Value = 7106,
        //    Index = 1
        //  }
        //};

        List<TXOutputWallet> outputsSpendable = OutputsSpendable
          .Where(o => o.Value > feePerTXInput)
          .Concat(OutputsSpendableUnconfirmed.Where(o => o.Value > feePerTXInput))
          .Except(OutputsSpentUnconfirmed, new EqualityComparerTXOutputWallet())
          .Take(VarInt.PREFIX_UINT16 - 1).ToList();

        value = outputsSpendable.Sum(o => o.Value);
        feeTXInputScaffold = feePerTXInput * outputsSpendable.Count;

        if (value - feeTXInputScaffold < valueNettoMinimum)
          return false;

        tXRaw.AddRange(new byte[] { 0x01, 0x00, 0x00, 0x00 }); // version
        tXRaw.Add((byte)outputsSpendable.Count);

        foreach (TXOutputWallet tXOutputWallet in outputsSpendable)
        {
          tXRaw.AddRange(tXOutputWallet.TXID);
          tXRaw.AddRange(BitConverter.GetBytes(tXOutputWallet.Index));
          tXRaw.Add(0x00); // length empty script
          tXRaw.AddRange(BitConverter.GetBytes(sequence));
        }

        return true;
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