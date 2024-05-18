﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;

namespace BTokenLib
{
  public class TXBTokenValueTransfer : TXBToken
  {
    public List<TXOutputBToken> TXOutputs = new();
    public bool IsCoinbase;


    public TXBTokenValueTransfer()
    { }

    public TXBTokenValueTransfer(byte[] buffer, SHA256 sHA256, bool flagIsCoinbase = false)
    {
      IsCoinbase = flagIsCoinbase;

      int index = 1;

      if (!IsCoinbase)
        ParseTXBTokenInput(buffer, ref index, sHA256);

      int countOutputs = VarInt.GetInt(buffer, ref index);

      for (int i = 0; i < countOutputs; i += 1)
      {
        TXOutputBToken tXOutput = new(buffer, ref index);
        TXOutputs.Add(tXOutput);

        Value += tXOutput.Value;
      }

      if (!IsCoinbase)
        VerifySignatureTX(buffer, ref index);

      Hash = sHA256.ComputeHash(
       sHA256.ComputeHash(buffer));
    }
  }
}
