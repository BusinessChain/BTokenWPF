using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Security.Cryptography;

namespace BTokenLib
{
  public class TXBTokenCoinbase : TXBToken
  {
    public int BlockHeight;
    public List<TXOutputBToken> TXOutputs = new();


    public TXBTokenCoinbase()
    { }

    public TXBTokenCoinbase(byte[] tXRaw, SHA256 sHA256)
    {
      int index = 1;

      BlockHeight = BitConverter.ToInt32(tXRaw, index);
      index += 4;

      int countOutputs = VarInt.GetInt(tXRaw, ref index);

      for (int i = 0; i < countOutputs; i += 1)
      {
        TXOutputBToken tXOutput = new(tXRaw, ref index);
        TXOutputs.Add(tXOutput);

        Value += tXOutput.Value;
      }

      TXRaw = tXRaw.ToList();
      Hash = sHA256.ComputeHash(sHA256.ComputeHash(tXRaw));
    }

    public override List<(string label, string value)> GetLabelsValuePairs()
    {
      List<(string label, string value)> labelValuePairs = base.GetLabelsValuePairs();

      for (int i = 0; i < TXOutputs.Count; i += 1)
      {
        TXOutputBToken output = TXOutputs[i];

        labelValuePairs.Add(($"Output{i} :: IDAccount", $"{output.IDAccount.BinaryToBase58Check()}"));
        labelValuePairs.Add(($"Output{i} :: Value", $"{Value}"));
      }

      return labelValuePairs;
    }
  }
}
