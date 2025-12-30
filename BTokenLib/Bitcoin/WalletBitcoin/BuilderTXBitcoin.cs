using System;
using System.Text;
using System.Linq;
using System.Collections.Generic;

namespace BTokenLib
{
  public partial class TokenBitcoin : Token
  {
    public partial class WalletBitcoin : Wallet
    {
      abstract class BuilderTXBitcoin
      {
        public byte[] TXRaw;

        public byte[] KeyPublicSource;

        public double FeePerByte;
        public long Fee;

        public BuilderTXBitcoin(byte[] keyPublicSource, double feePerByte)
        {
          KeyPublicSource = keyPublicSource;
          FeePerByte = feePerByte;
        }

        public void SignTX(Wallet wallet, List<byte> tXRaw)
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

            byte[] signature = wallet.GetSignature(message);

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
      }

      class BuilderTXBitcoinValue : BuilderTXBitcoin
      {
        const int LENGTH_P2PKH_OUTPUT = 34;
        const int LENGTH_P2PKH_INPUT = 148;
        const int LENGTH_P2PKH_OVERHEAD = 10;

        public const byte LENGTH_SCRIPT_P2PKH = 25;
        public static byte[] PREFIX_P2PKH = new byte[] { 0x76, 0xA9, 0x14 };
        public static byte[] POSTFIX_P2PKH = new byte[] { 0x88, 0xAC };

        public BuilderTXBitcoinValue(
          Wallet wallet,
          byte[] keyPublicSource,
          string addressDest,
          List<TXOutputWallet> outputsSpendable,
          long valueOutput,
          double feePerByte,
          int sequence)
          : base(keyPublicSource, feePerByte)
        {
          long valueInputs = outputsSpendable.Sum(o => o.Value);

          long feeTX = (long)(feePerByte * LENGTH_P2PKH_INPUT * outputsSpendable.Count)
            + (long)(LENGTH_P2PKH_OUTPUT * feePerByte)
            + LENGTH_P2PKH_OVERHEAD;

          if (valueInputs < feeTX)
            throw new ProtocolException(
              $"Not enough funds held in unspent outputs: {valueInputs} sats." +
              $"Fee required by P2PKH transaction: {feeTX}. Reduce specified rate for fee per byte.");

          // The premis is that the value of the change output has to be greater than the fee of one output,
          // so that a future spend of that output is economically feasible.
          long valueChange = valueInputs - valueOutput - feeTX - (long)(LENGTH_P2PKH_OUTPUT * feePerByte);
          bool flagCreateOutputChange = valueChange > (long)(LENGTH_P2PKH_OUTPUT * feePerByte);

          List<byte> tXRaw = new();

          tXRaw.AddRange(new byte[] { 0x01, 0x00, 0x00, 0x00 }); // version
          tXRaw.Add((byte)outputsSpendable.Count);

          foreach (TXOutputWallet tXOutputWallet in outputsSpendable)
          {
            tXRaw.AddRange(tXOutputWallet.TXID);
            tXRaw.AddRange(BitConverter.GetBytes(tXOutputWallet.Index));
            tXRaw.Add(0x00); // length empty script
            tXRaw.AddRange(BitConverter.GetBytes(sequence));
          }

          if (flagCreateOutputChange)
          {
            tXRaw.Add(0x02);

            tXRaw.AddRange(BitConverter.GetBytes(valueChange));
            tXRaw.Add((byte)PublicScript.Length);
            tXRaw.AddRange(PublicScript);
          }
          else
            tXRaw.Add(0x01);

          tXRaw.AddRange(BitConverter.GetBytes(valueOutput));

          tXRaw.Add(LENGTH_SCRIPT_P2PKH);
          tXRaw.AddRange(PREFIX_P2PKH);
          tXRaw.AddRange(addressDest.Base58CheckToPubKeyHash());
          tXRaw.AddRange(POSTFIX_P2PKH);

          tXRaw.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x00 }); // locktime
          tXRaw.AddRange(new byte[] { 0x01, 0x00, 0x00, 0x00 }); // sighash

          SignTX(wallet, tXRaw);
        }
      }
    }
  }
}
