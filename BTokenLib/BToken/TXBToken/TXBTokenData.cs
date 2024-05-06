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

    public TXBTokenData(byte[] buffer, SHA256 sHA256)
    {
      int index = 1;

      ParseTXBTokenInput(buffer, ref index, sHA256);

      Data = new byte[VarInt.GetInt(buffer, ref index)];

      Array.Copy(buffer, index, Data, 0, Data.Length);

      index += Data.Length;

      VerifySignatureTX(buffer, ref index);

      Hash = sHA256.ComputeHash(
       sHA256.ComputeHash(buffer));
    }
  }
}
