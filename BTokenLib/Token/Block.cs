using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;


namespace BTokenLib
{
  public class Block
  {
    Token Token;

    public Header Header;

    public SHA256 SHA256 = SHA256.Create();

    public List<TX> TXs = new();

    public byte[] Buffer;
    public int LengthBufferPayload;


    public Block(Token token)
    {
      Token = token;
      Buffer = new byte[Token.SizeBlockMax];
    }

    public void Parse()
    {
      int startIndex = 0;

      Header = Token.ParseHeader(Buffer, ref startIndex);

      ParseTXs(Buffer, ref startIndex);
    }

    public void ParseTXs(byte[] buffer, ref int startIndex)
    {
      int tXCount = VarInt.GetInt(buffer, ref startIndex);

      int startIndexBeginningOfTXs = startIndex;

      if (tXCount == 0)
        throw new ProtocolException($"Block {this} lacks coinbase transaction.");

      if (tXCount == 1)
      {
        TX tX = Token.ParseTX(buffer, ref startIndex, SHA256);
        TXs.Add(tX);
      }
      else
      {
        int tXsLengthMod2 = tXCount & 1;
        var merkleList = new byte[tXCount + tXsLengthMod2][];

        for (int t = 0; t < tXCount; t += 1)
        {
          TX tX = Token.ParseTX(buffer, ref startIndex, SHA256);
          TXs.Add(tX);

          merkleList[t] = tX.Hash;
        }

        if (tXsLengthMod2 != 0)
          merkleList[tXCount] = merkleList[tXCount - 1];
      }

      if (!Header.MerkleRoot.IsAllBytesEqual(ComputeMerkleRoot()))
        throw new ProtocolException("Payload hash not equal to merkle root.");

      Header.CountTXs = TXs.Count;
      Header.CountBytesTXs = startIndex - startIndexBeginningOfTXs;
      Header.Fee = TXs.Sum(t => t.Fee);
    }

    public byte[] ComputeMerkleRoot()
    {
      const int HASH_BYTE_SIZE = 32;

      if (TXs.Count == 1)
        return TXs[0].Hash;

      int tXsLengthMod2 = TXs.Count & 1;
      var merkleList = new byte[TXs.Count + tXsLengthMod2][];
      int merkleIndex = merkleList.Length;

      for (int i = 0; i < TXs.Count; i += 1)
        merkleList[i] = TXs[i].Hash;

      if (tXsLengthMod2 != 0)
        merkleList[TXs.Count] = merkleList[TXs.Count - 1];

      byte[] leafPair = new byte[2 * HASH_BYTE_SIZE];

      while (true)
      {
        merkleIndex >>= 1;
        
        for (int i = 0; i < merkleIndex; i++)
        {
          int i2 = i << 1;
          merkleList[i2].CopyTo(leafPair, 0);
          merkleList[i2 + 1].CopyTo(leafPair, HASH_BYTE_SIZE);

          merkleList[i] =
            SHA256.ComputeHash(
              SHA256.ComputeHash(leafPair));
        }

        if (merkleIndex == 1)
          return merkleList[0];

        if ((merkleIndex & 1) != 0)
        {
          merkleList[merkleIndex] = merkleList[merkleIndex - 1];
          merkleIndex += 1;
        }
      }
    }

    public void Serialize()
    {
      int startIndex = 0;

      byte[] bufferHeader = Header.Serialize();

      bufferHeader.CopyTo(Buffer, startIndex);
      startIndex += bufferHeader.Length;

      byte[] countTXs = VarInt.GetBytes(TXs.Count);
      countTXs.CopyTo(Buffer, startIndex);
      startIndex += countTXs.Length;

      for(int i = 0; i < TXs.Count; i += 1)
      {
        TXs[i].TXRaw.CopyTo(Buffer, startIndex);
        startIndex += TXs[i].TXRaw.Length;
      }

      LengthBufferPayload = startIndex;
    }

    public void WriteToStream(Stream stream)
    {
      stream.Write(Buffer, 0, LengthBufferPayload);
      stream.Flush();
    }

    public override string ToString()
    {
      return Header.ToString();
    }

    public void Clear()
    {
      TXs.Clear();
    }
  }
}
