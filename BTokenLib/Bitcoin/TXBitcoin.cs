using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;


namespace BTokenLib
{
  public class TXBitcoin : TX
  {
    public List<TXInputBitcoin> Inputs = new();
    public List<TXOutputBitcoin> TXOutputs = new();


    public override bool IsSuccessorTo(TX tX)
    {
      TXBitcoin tXBitcoin = tX as TXBitcoin;

      if(tXBitcoin != null)
      {
        foreach(TXInputBitcoin tXInput in Inputs)
          if (tXInput.TXIDOutput.IsAllBytesEqual(tX.Hash))
            return true;
      }

      return false;
    }

    public override TokenAnchor GetAnchorToken()
    {
      return TXOutputs[0].TokenAnchor;
    }

    public override string Print()
    {
      string text = "";

      return text;
    }

    public override void WriteToStream(Stream stream)
    {
      stream.Write(TXRaw.ToArray(), 0, TXRaw.Count);
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

      labelValuePairs.Add(($"TXRaw", $"{TXRaw.ToArray().Reverse().ToArray().ToHexString()}"));

      return labelValuePairs;
    }
  }
}
