using System;
using System.IO;


namespace BTokenLib
{
  public class Account
  {
    public const int LENGTH_ACCOUNT = 36;
    public const int LENGTH_ID = 20;

    public byte[] ID;
    public int BlockHeightAccountInit;
    public int Nonce;
    public long Value;


    public Account() { }

    public Account(byte[] buffer, ref int startIndex)
    {
      Array.Copy(buffer, startIndex, ID, 0, LENGTH_ID);
      startIndex += LENGTH_ID;

      BlockHeightAccountInit = BitConverter.ToInt32(buffer, startIndex);
      startIndex += 4;

      Nonce = BitConverter.ToInt32(buffer, startIndex);
      startIndex += 4;

      Value = BitConverter.ToInt64(buffer, startIndex);
      startIndex += 8;
    }

    public Account(FileStream fileStream)
    {
      ID = new byte[LENGTH_ID];
      fileStream.Read(ID, 0, LENGTH_ID);

      BlockHeightAccountInit = fileStream.ReadInt32();
      Nonce = fileStream.ReadInt32();
      Value = fileStream.ReadInt64();
    }

    public override string ToString()
    {
      return ID.BinaryToBase58Check();
    }

    public byte[] Serialize()
    {
      int startIndex = 0;
      byte[] buffer = new byte[LENGTH_ACCOUNT];

      Serialize(buffer, ref startIndex);

      return buffer;
    }

    public void Serialize(byte[] buffer, ref int startIndex)
    {
      ID.CopyTo(buffer, startIndex);
      startIndex += LENGTH_ID;

      BitConverter.GetBytes(BlockHeightAccountInit).CopyTo(buffer, startIndex);
      startIndex += 4;

      BitConverter.GetBytes(Nonce).CopyTo(buffer, startIndex);
      startIndex += 4;

      BitConverter.GetBytes(Value).CopyTo(buffer, startIndex);
      startIndex += 8;
    }

    public void Serialize(FileStream stream)
    {
      stream.Write(ID);
      stream.Write(BitConverter.GetBytes(BlockHeightAccountInit));
      stream.Write(BitConverter.GetBytes(Nonce));
      stream.Write(BitConverter.GetBytes(Value));
    }

  }
}