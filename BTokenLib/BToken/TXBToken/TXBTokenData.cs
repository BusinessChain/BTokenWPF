using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;

namespace BTokenLib
{
  public class TXBTokenData : TXBToken
  {
    public byte[] Data;


    public TXBTokenData()
    { }

    public TXBTokenData(byte[] tXRaw, SHA256 sHA256)
    {
      int index = 1;

      ParseTXBTokenInput(tXRaw, ref index, sHA256);

      Data = new byte[VarInt.GetInt(tXRaw, ref index)];

      Array.Copy(tXRaw, index, Data, 0, Data.Length);

      index += Data.Length;

      VerifySignatureTX(tXRaw, ref index);

      TXRaw = tXRaw.ToList();

      Hash = sHA256.ComputeHash(
       sHA256.ComputeHash(tXRaw));
    }
  }
}
