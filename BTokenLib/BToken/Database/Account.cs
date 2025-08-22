using System;
using System.IO;

using LiteDB;


namespace BTokenLib
{
  public partial class TokenBToken : Token
  {
    public class Account
    {
      public const int LENGTH_ACCOUNT = 40;
      public const int LENGTH_ID = 20;

      [BsonId]
      public byte[] ID = new byte[LENGTH_ID];

      [BsonField]
      public int BlockHeightAccountCreated;

      [BsonField]
      public int BlockHeightLastUpdatedOnDisk;

      [BsonField]
      public int Nonce;

      [BsonField]
      public long Balance;

      public long PositionInFileStream = -1;
      byte[] ByteArraySerialized = new byte[LENGTH_ACCOUNT];


      public Account() { }

      public Account(Account account) 
      {
        ID = account.ID;
        BlockHeightAccountCreated = account.BlockHeightAccountCreated;
        Nonce = account.Nonce;
        Balance = account.Balance;
      }

      public Account(FileStream fileStream)
      {
        PositionInFileStream = fileStream.Position;
        fileStream.Read(ByteArraySerialized, 0, LENGTH_ACCOUNT);

        int index = 0;

        Array.Copy(ByteArraySerialized, ID, LENGTH_ID);
        index += LENGTH_ID;

        BlockHeightAccountCreated = BitConverter.ToInt32(ByteArraySerialized, index);
        index += 4;

        Nonce = BitConverter.ToInt32(ByteArraySerialized, index);
        index += 4;

        Balance = BitConverter.ToInt64(ByteArraySerialized, index);
      }

      public void FlushToDisk(FileStream fileStream)
      {
        fileStream.Position = PositionInFileStream;

        Serialize();

        fileStream.Write(ByteArraySerialized, 0, ByteArraySerialized.Length);
        fileStream.Flush(true);
      }

      public void SpendTX(TXBToken tX)
      {
        if (BlockHeightAccountCreated != tX.BlockheightAccountInit || Nonce != tX.Nonce)
          throw new ProtocolException($"Staged account {this} referenced by TX {tX} has unequal nonce or blockheightAccountInit.");

        if (Balance < tX.GetValueOutputs() + tX.Fee)
          throw new ProtocolException($"Staged account {this} referenced by TX {tX} does not have enough fund.");

        Nonce += 1;
        Balance -= tX.GetValueOutputs() + tX.Fee;
      }

      public void ReverseSpendTX(TXBToken tX)
      {
        Nonce -= 1;
        Balance += tX.GetValueOutputs() + tX.Fee;
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

        BitConverter.GetBytes(BlockHeightAccountCreated).CopyTo(ByteArraySerialized, index);
        index += 4;

        BitConverter.GetBytes(Nonce).CopyTo(ByteArraySerialized, index);
        index += 4;

        BitConverter.GetBytes(Balance).CopyTo(ByteArraySerialized, index);

        return ByteArraySerialized;
      }
    }
  }
}