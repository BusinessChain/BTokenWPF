using System;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;


namespace BTokenLib
{
  public partial class TokenBitcoin : Token
  {
    public class TXBitcoin : TX
    {
      public List<TXInputBitcoin> Inputs = new();
      public List<TXOutputBitcoin> TXOutputs = new();


      public void Serialize(WalletBitcoin wallet)
      {
        List<byte> tXRaw = new();

        tXRaw.AddRange(new byte[] { 0x01, 0x00, 0x00, 0x00 }); // version

        tXRaw.Add((byte)Inputs.Count);
        foreach (TXInputBitcoin input in Inputs)
        {
          tXRaw.AddRange(input.TXIDOutput);
          tXRaw.AddRange(BitConverter.GetBytes(input.OutputIndex));
          tXRaw.Add(0x00); // length empty script
          tXRaw.AddRange(BitConverter.GetBytes(input.Sequence));
        }

        tXRaw.Add((byte)TXOutputs.Count);
        foreach(TXOutputBitcoin output in TXOutputs)
        {
          tXRaw.AddRange(BitConverter.GetBytes(output.Value));
          tXRaw.AddRange(VarInt.GetBytes(output.Script.Length));
          tXRaw.AddRange(output.Script);
        }

        tXRaw.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x00 }); // locktime
        tXRaw.AddRange(new byte[] { 0x01, 0x00, 0x00, 0x00 }); // sighash

        SignTX(tXRaw, wallet);

        TXRaw = tXRaw.ToArray();
      }

      void SignTX(List<byte> tXRaw, WalletBitcoin wallet)
      {
        List<List<byte>> signaturesPerInput = new();
        int indexFirstInput = 5;

        for (int i = 0; i < Inputs.Count; i++)
        {
          List<byte> tXRawSign = tXRaw.ToList();
          int indexRawSign = indexFirstInput + 36 * (i + 1) + 5 * i;

          tXRawSign[indexRawSign++] = (byte)wallet.PublicScript.Length;
          tXRawSign.InsertRange(indexRawSign, wallet.PublicScript);

          byte[] message = wallet.SHA256.ComputeHash(tXRawSign.ToArray());

          byte[] signature = wallet.GetSignature(message);

          List<byte> scriptSig = new();

          scriptSig.Add((byte)(signature.Length + 1));
          scriptSig.AddRange(signature);
          scriptSig.Add(0x01);

          scriptSig.Add((byte)wallet.KeyPublic.Length);
          scriptSig.AddRange(wallet.KeyPublic);

          signaturesPerInput.Add(scriptSig);
        }

        for (int i = Inputs.Count - 1; i >= 0; i -= 1)
        {
          int indexSig = indexFirstInput + 36 * (i + 1) + 5 * i;

          tXRaw[indexSig++] = (byte)signaturesPerInput[i].Count;

          tXRaw.InsertRange(
            indexSig,
            signaturesPerInput[i]);
        }

        tXRaw.RemoveRange(tXRaw.Count - 4, 4);
      }

      public override bool IsSuccessorTo(TX tX)
      {
        if (tX is TXBitcoin tXBitcoin)
          foreach (TXInputBitcoin tXInput in Inputs)
            if (tXInput.TXIDOutput.IsAllBytesEqual(tX.Hash))
              return true;

        return false;
      }

      public override long GetValueOutputs()
      {
        return TXOutputs.Sum(o => o.Value);
      }

      public override List<TokenAnchor> GetTokenAnchors()
      {
        return TXOutputs.Where(t => t.TokenAnchor != null).Select(t => t.TokenAnchor).ToList();
      }

      public override List<(string label, string value)> GetLabelsValuePairs()
      {
        List<(string label, string value)> labelValuePairs = new()
      {
        ("Hash", $"{this}")
      };

        for (int i = 0; i < Inputs.Count; i++)
        {
          TXInputBitcoin tXInput = Inputs[i];
          labelValuePairs.Add(($"Input{i} :: TXIDOutput", $"{tXInput.TXIDOutput.ToHexString()}"));
          labelValuePairs.Add(($"Input{i} :: OutputIndex", $"{tXInput.OutputIndex}"));
          labelValuePairs.Add(($"Input{i} :: Sequence", $"{tXInput.Sequence}"));
        }

        for (int i = 0; i < TXOutputs.Count; i++)
        {
          TXOutputBitcoin output = TXOutputs[i];

          labelValuePairs.Add(($"Output{i} :: Type", $"{output.Type}"));

          if (output.Type == TXOutputBitcoin.TypesToken.P2PKH)
          {
            labelValuePairs.Add(($"Output{i} :: PublicKeyHash160", $"{output.PublicKeyHash160.BinaryToBase58Check()}"));
            labelValuePairs.Add(($"Output{i} :: Value", $"{output.Value}"));
          }
          else if (output.Type == TXOutputBitcoin.TypesToken.AnchorToken)
          {
            labelValuePairs.Add(($"Output{i} :: IDToken", $"{output.TokenAnchor.IDToken.ToHexString()}"));
            labelValuePairs.Add(($"Output{i} :: HashBlockReferenced", $"{output.TokenAnchor.HashBlockReferenced.ToHexString()}"));
            labelValuePairs.Add(($"Output{i} :: HashBlockPreviousReferenced", $"{output.TokenAnchor.HashBlockPreviousReferenced.ToHexString()}"));
          }
        }
        return labelValuePairs;
      }
    }
  }
}