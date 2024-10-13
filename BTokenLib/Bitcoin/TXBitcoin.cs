using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;


namespace BTokenLib
{
  public class TXBitcoin : TX
  {
    public List<TXInputBitcoin> Inputs = new();
    public List<TXOutputBitcoin> TXOutputs = new();

    // Wird gebraucht für tx welche gerelayd werden. In Bitcoin womöglich nicht gemacht.
    // weil nur non- listening node.
    public List<byte> TXRaw = new();


    public override int GetSequence()
    {
      return Inputs.First().Sequence;
    }

    public override bool IsSuccessorTo(TX tX)
    {
      TXBitcoin tXBitcoin = tX as TXBitcoin;

      if(tXBitcoin != null)
        foreach (TXInputBitcoin tXInput in Inputs)
          if (tXInput.TXIDOutput.IsAllBytesEqual(tX.Hash))
            return true;

      return false;
    }

    public override bool IsReplacementByFeeFor(TX tX)
    {
      if (tX is TXBitcoin tXBitcoin)
        foreach (TXInputBitcoin tXInput in Inputs)
          foreach (TXInputBitcoin tXBitcoinInput in tXBitcoin.Inputs)
            if (tXInput.TXIDOutput.IsAllBytesEqual(tXBitcoinInput.TXIDOutput) &&
                tXInput.OutputIndex == tXBitcoinInput.OutputIndex &&
                tXInput.Sequence > tXBitcoinInput.Sequence)
              return true;

      return false;
    }

    public override bool TryGetAnchorToken(out TokenAnchor tokenAnchor)
    {
      tokenAnchor = TXOutputs[0].TokenAnchor;
      return tokenAnchor != null;
    }

    public override string Print()
    {
      string text = "";

      return text;
    }

    public override void WriteToStream(Stream stream)
    {
      byte[] tXRaw = Serialize();
      stream.Write(tXRaw, 0, tXRaw.Length);
    }

    public override byte[] Serialize()
    {
      List<byte> tXRaw = new();

      // Serialize version
      tXRaw.AddRange(new byte[] { 0x01, 0x00, 0x00, 0x00 });
      tXRaw.Add((byte)Inputs.Count);

      foreach (var input in Inputs)
      {
        tXRaw.AddRange(input.TXIDOutput);
        tXRaw.AddRange(BitConverter.GetBytes(input.OutputIndex));
        tXRaw.Add((byte)0x00);
        tXRaw.AddRange(BitConverter.GetBytes(input.Sequence));
      }
      tXRaw.Add((byte)TXOutputs.Count);

      foreach (TXOutputBitcoin output in TXOutputs)
      {
        writer.Write(output.Value);

        if (output.Type == TXOutputBitcoin.TypesToken.ValueTransfer)
        {
          writer.Write(WalletBitcoin.LENGTH_SCRIPT_P2PKH);
          writer.Write(WalletBitcoin.PREFIX_P2PKH);
          writer.Write(output.PublicKeyHash160);
          writer.Write(WalletBitcoin.POSTFIX_P2PKH);
        }
        else if (output.Type == TXOutputBitcoin.TypesToken.AnchorToken)
        {

        }
        else
        {

        }
      }

      tXRaw.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x00 }); // locktime
      tXRaw.AddRange(new byte[] { 0x01, 0x00, 0x00, 0x00 }); // sighash
    }

    public override List<(string label, string value)> GetLabelsValuePairs()
    {
      List<(string label, string value)> labelValuePairs = new()
      {
        ("Hash", $"{this}")
      };

      for (int i = 0; i < Inputs.Count; i += 1)
      {
        TXInputBitcoin tXInput = Inputs[i];
        labelValuePairs.Add(($"Input{i} :: TXIDOutput", $"{tXInput.TXIDOutput.ToHexString()}"));
        labelValuePairs.Add(($"Input{i} :: OutputIndex", $"{tXInput.OutputIndex}"));
        labelValuePairs.Add(($"Input{i} :: Sequence", $"{tXInput.Sequence}"));
      }

      for (int i = 0; i < TXOutputs.Count; i += 1)
      {
        TXOutputBitcoin output = TXOutputs[i];

        labelValuePairs.Add(($"Output{i} :: Type", $"{output.Type}"));

        if (output.Type == TXOutputBitcoin.TypesToken.ValueTransfer)
        {
          labelValuePairs.Add(($"Output{i} :: PublicKeyHash160", $"{output.PublicKeyHash160.BinaryToBase58Check()}"));
          labelValuePairs.Add(($"Output{i} :: Value", $"{output.Value}"));
        }
        else if(output.Type == TXOutputBitcoin.TypesToken.AnchorToken)
        {
          labelValuePairs.Add(($"Output{i} :: IDToken", $"{output.TokenAnchor.IDToken.ToHexString()}"));
          labelValuePairs.Add(($"Output{i} :: HashBlockReferenced", $"{output.TokenAnchor.HashBlockReferenced.ToHexString()}"));
          labelValuePairs.Add(($"Output{i} :: HashBlockPreviousReferenced", $"{output.TokenAnchor.HashBlockPreviousReferenced.ToHexString()}"));
        }
      }

      labelValuePairs.Add(($"TXRaw", $"{Serialize().Reverse().ToArray().ToHexString()}"));

      return labelValuePairs;
    }
  }
}