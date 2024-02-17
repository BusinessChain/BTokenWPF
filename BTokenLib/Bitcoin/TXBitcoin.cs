using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Security.Cryptography;


namespace BTokenLib
{
  public class TXBitcoin : TX
  {
    public List<TXInput> Inputs = new();


    public override string Print()
    {
      string text = "";

      foreach (TXOutputBitcoin tXOutput in TXOutputs)
        if (tXOutput.Value == 0)
        {
          text += $"\t{this}\t";

          int index = tXOutput.StartIndexScript + 4;

          text += $"\t{tXOutput.Buffer.Skip(index).Take(32).ToArray().ToHexString()}\n";
        }

      return text;
    }

    void Serialize(Wallet wallet, SHA256 sHA256, byte[] dataAnchorToken)
    {
      TXRaw.AddRange(new byte[] { 0x01, 0x00, 0x00, 0x00 }); // version
      TXRaw.AddRange(VarInt.GetBytes(Inputs.Count));

      int indexFirstInput = TXRaw.Count;

      for (int i = 0; i < Inputs.Count; i += 1)
      {
        TXRaw.AddRange(Inputs[i].TXIDOutput);
        TXRaw.AddRange(BitConverter.GetBytes(Inputs[i].OutputIndex));
        TXRaw.Add(0x00); // length empty script
        TXRaw.AddRange(BitConverter.GetBytes(Inputs[i].Sequence)); // sequence
      }

      TXRaw.Add((byte)TXOutputs.Count);

      foreach(TXOutput tXOutput in TXOutputs)
        TXRaw.AddRange(tXOutput.Buffer);

      TXRaw.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x00 }); // locktime
      TXRaw.AddRange(new byte[] { 0x01, 0x00, 0x00, 0x00 }); // sighash

      List<List<byte>> signaturesPerInput = new();

      for (int i = 0; i < Inputs.Count; i += 1)
      {
        List<byte> tXRawSign = TXRaw.ToList();
        int indexRawSign = indexFirstInput + 36 * (i + 1) + 5 * i;

        tXRawSign[indexRawSign++] = (byte)wallet.PublicScript.Length;
        tXRawSign.InsertRange(indexRawSign, wallet.PublicScript);

        signaturesPerInput.Add(
          wallet.GetScriptSignature(tXRawSign.ToArray()));
      }

      for (int i = Inputs.Count - 1; i >= 0; i -= 1)
      {
        int indexSign = indexFirstInput + 36 * (i + 1) + 5 * i;

        TXRaw[indexSign++] = (byte)signaturesPerInput[i].Count;

        TXRaw.InsertRange(
          indexSign,
          signaturesPerInput[i]);
      }

      TXRaw.RemoveRange(TXRaw.Count - 4, 4);

      Hash = sHA256.ComputeHash(
       sHA256.ComputeHash(TXRaw.ToArray()));
    }
  }
}
