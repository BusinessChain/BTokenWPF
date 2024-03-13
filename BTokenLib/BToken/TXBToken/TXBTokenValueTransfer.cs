using System;
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

    public TXBTokenValueTransfer()
    { }

    public TXBTokenValueTransfer(byte[] buffer, int startIndexMessage, ref int index, SHA256 sHA256)
    {
      ParseTXBTokenInput(buffer, ref index, sHA256);

      int countOutputs = VarInt.GetInt32(buffer, ref index);

      for (int i = 0; i < countOutputs; i += 1)
      {
        TXOutputBToken tXOutput = new(buffer, ref index);
        TXOutputs.Add(tXOutput);

        Value += tXOutput.Value;
      }

      VerifySignatureTX(buffer, startIndexMessage, ref index);
    }
  }
}
