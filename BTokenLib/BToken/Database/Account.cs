using System;
using System.IO;


namespace BTokenLib
{
  public class Account
  {
    public const int LENGTH_ACCOUNT = 36;
    public const int LENGTH_ID = 20;

    byte[] ByteArraySerialized = new byte[LENGTH_ACCOUNT];

    public byte[] ID = new byte[LENGTH_ID];
    public int BlockHeightAccountInit;
    public int Nonce;
    public long Value;

    public long StartIndexFileDBOrigin = -1;


    public Account() { }

    public Account(FileStream fileStream)
    {
      StartIndexFileDBOrigin = fileStream.Position;
      fileStream.Read(ByteArraySerialized, 0, LENGTH_ACCOUNT);

      int index = 0;

      Array.Copy(ByteArraySerialized, ID, LENGTH_ID);
      index += LENGTH_ID;

      BlockHeightAccountInit = BitConverter.ToInt32(ByteArraySerialized, index);
      index += 4;

      Nonce = BitConverter.ToInt32(ByteArraySerialized, index);
      index += 4;

      Value = BitConverter.ToInt64(ByteArraySerialized, index);
    }


    public void AddValue(long value)
    {
      Value += value;
    }

    public void SpendTX(TXBToken tX)
    {
      if (BlockHeightAccountInit != tX.BlockheightAccountInit || Nonce != tX.Nonce)
        throw new ProtocolException($"Staged account {this} referenced by TX {tX} has unequal nonce or blockheightAccountInit.");

      if (Value < tX.GetValueOutputs() + tX.Fee)
        throw new ProtocolException($"Staged account {this} referenced by TX {tX} does not have enough fund.");

      Nonce += 1;
      Value -= tX.GetValueOutputs() + tX.Fee;
    }

    public override string ToString()
    {
      return ID.BinaryToBase58Check();
    }

    public byte[] Serialize()
    {
      int index = 0;

      ID.CopyTo(ByteArraySerialized, index);
      index += LENGTH_ID;

      BitConverter.GetBytes(BlockHeightAccountInit).CopyTo(ByteArraySerialized, index);
      index += 4;

      BitConverter.GetBytes(Nonce).CopyTo(ByteArraySerialized, index);
      index += 4;

      BitConverter.GetBytes(Value).CopyTo(ByteArraySerialized, index);

      return ByteArraySerialized;
    }
  }
}