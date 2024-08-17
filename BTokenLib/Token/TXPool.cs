using System;
using System.IO;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace BTokenLib
{
  public abstract class TXPool
  {
    public Token Token;
    public FileStream FileTXPoolDict;



    public TXPool(Token token)
    {
      Token = token;

      FileTXPoolDict = new FileStream(
        Path.Combine(token.GetName(), "TXPoolDict"),
        FileMode.OpenOrCreate,
        FileAccess.ReadWrite,
        FileShare.Read);
    }

    public void Load()
    {
      SHA256 sHA256 = SHA256.Create();

      while (FileTXPoolDict.Position < FileTXPoolDict.Length)
      {
        TX tX = Token.ParseTX(FileTXPoolDict, sHA256);

        if(TryAddTX(tX))
        {
          Token.SendAnchorTokenUnconfirmedToChilds(tX);
          Token.Wallet.InsertTXUnconfirmed(tX);
        }
      }
    }

    public abstract void RemoveTXs(IEnumerable<byte[]> hashesTX);

    public abstract bool TryAddTX(TX tX);

    public abstract bool TryGetTX(byte[] hashTX, out TX tX);

    public abstract List<TX> GetTXs(int countMax, out long feeTXs);
  }
}
