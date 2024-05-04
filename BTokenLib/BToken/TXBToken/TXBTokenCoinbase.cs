using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace BTokenLib
{
  public class TXBTokenCoinbase : TXBToken
  {
    public const int TypeTX = 0;

    public List<TXOutputBToken> TXOutputs = new();


    public TXBTokenCoinbase()
    { }

    public TXBTokenCoinbase(long blockReward, byte[] account, SHA256 sHA256)
    {
      TXOutputs.Add(new TXOutputBToken(blockReward, account));

      TXRaw.AddRange(BitConverter.GetBytes(TypeTX)); // token ; config

      TXRaw.Add((byte)TXOutputs.Count); // count outputs

      TXRaw.AddRange(BitConverter.GetBytes(blockReward));
      TXRaw.AddRange(account);

      Hash = sHA256.ComputeHash(
       sHA256.ComputeHash(TXRaw.ToArray()));
    }

    public TXBTokenCoinbase(byte[] buffer, ref int index)
    {
      int countOutputs = VarInt.GetInt(buffer, ref index);

      for (int i = 0; i < countOutputs; i += 1)
      {
        TXOutputBToken tXOutput = new(buffer, ref index);
        TXOutputs.Add(tXOutput);

        Value += tXOutput.Value;
      }
    }
  }
}
