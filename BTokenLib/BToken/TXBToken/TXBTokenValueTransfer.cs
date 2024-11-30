using System;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace BTokenLib
{
  public class TXBTokenValueTransfer : TXBToken
  {
    public List<TXOutputBToken> TXOutputs = new();


    public TXBTokenValueTransfer(byte[] buffer, ref int index, SHA256 sHA256)
    {
      int indexTxStart = index - 1;

      ParseTXBTokenInput(buffer, ref index, sHA256);

      int countOutputs = VarInt.GetInt(buffer, ref index);

      for (int i = 0; i < countOutputs; i += 1)
        TXOutputs.Add(new(buffer, ref index));

      CountBytes = index - indexTxStart;

      Hash = sHA256.ComputeHash(sHA256.ComputeHash(buffer, indexTxStart, CountBytes));

      VerifySignatureTX(indexTxStart, buffer, ref index);
    }

    public override long GetValue()
    {
      return base.GetValue() + TXOutputs.Sum(t => t.Value);
    }

    public override List<(string label, string value)> GetLabelsValuePairs()
    {
      List<(string label, string value)> labelValuePairs = base.GetLabelsValuePairs();

      for (int i = 0; i < TXOutputs.Count; i += 1)
      {
        TXOutputBToken output = TXOutputs[i];

        labelValuePairs.Add(($"Output{i} :: IDAccount", $"{output.IDAccount.BinaryToBase58Check()}"));
        labelValuePairs.Add(($"Output{i} :: Value", $"{output.Value}"));
      }

      return labelValuePairs;
    }
  }
}
