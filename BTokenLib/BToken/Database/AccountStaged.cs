using System;

namespace BTokenLib
{
  public class AccountStaged
  {
    public Account Account;
    public CacheDB CacheDB;
    public FileDB FileDB;
    public long StartIndexAccountFileDB;
    public long Value;
    public int Nonce;


    public void SpendTX(TXBToken tX)
    {
      if (Account.BlockHeightAccountInit != tX.BlockheightAccountInit || Nonce != tX.Nonce)
        throw new ProtocolException($"Staged account {Account} referenced by TX {tX} has unequal nonce or blockheightAccountInit.");

      if (Value < tX.GetValueOutputs() + tX.Fee)
        throw new ProtocolException($"Staged account {Account} referenced by TX {tX} does not have enough fund.");

      Nonce += 1;
      Value -= tX.GetValueOutputs() + tX.Fee;
    }

    public void AddValue(long value)
    {
      Value += value;
    }
  }
}
