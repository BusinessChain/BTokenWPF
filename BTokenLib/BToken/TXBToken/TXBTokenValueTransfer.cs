﻿using System;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace BTokenLib
{
  public class TXBTokenValueTransfer : TXBToken
  {
    public List<TXOutputBToken> TXOutputs = new();


    public TXBTokenValueTransfer()
    { }

    public TXBTokenValueTransfer(byte[] tXRaw, SHA256 sHA256)
    {
      int index = 1;

      ParseTXBTokenInput(tXRaw, ref index, sHA256);

      int countOutputs = VarInt.GetInt(tXRaw, ref index);

      for (int i = 0; i < countOutputs; i += 1)
      {
        TXOutputBToken tXOutput = new(tXRaw, ref index);
        TXOutputs.Add(tXOutput);

        Value += tXOutput.Value;
      }

      VerifySignatureTX(tXRaw, ref index);

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
