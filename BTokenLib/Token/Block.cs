using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace BTokenLib
{
  public abstract class Block
  {
    Token Token;

    const int HASH_BYTE_SIZE = 32;

    public Header Header;
    public Block BlockNext;
    public Block BlockChild;

    public SHA256 SHA256 = SHA256.Create();

    public List<TX> TXs = new();


    public Block(Token token)
    {
      Token = token;
    }


    public void Parse(Stream stream)
    {
      long positionStreamStart = stream.Position;

      Header = ParseHeader(stream);

      ParseTXs(stream);

      Header.CountTXs = TXs.Count;
      Header.CountBytesBlock = (int)(stream.Position - positionStreamStart);
    }

    public abstract Header ParseHeader(byte[] buffer, ref int index);

    public abstract Header ParseHeader(Stream stream);

    void ParseTXs(Stream stream)
    {
      int tXCount = VarInt.GetInt(stream);

      if (tXCount == 0)
        throw new ProtocolException($"Block {this} lacks coinbase transaction.");

      if (tXCount == 1)
      {
        TX tX = Token.ParseTX(stream, SHA256);

        TXs.Add(tX);
      }
      else
      {
        int tXsLengthMod2 = tXCount & 1;
        var merkleList = new byte[tXCount + tXsLengthMod2][];

        TX tX = Token.ParseTX(stream, SHA256);

        TXs.Add(tX);

        merkleList[0] = tX.Hash;

        for (int t = 1; t < tXCount; t += 1)
        {
          tX = Token.ParseTX(stream, SHA256);

          TXs.Add(tX);

          merkleList[t] = tX.Hash;
        }

        if (tXsLengthMod2 != 0)
          merkleList[tXCount] = merkleList[tXCount - 1];
      }

      if (!Header.MerkleRoot.HasEqualElements(ComputeMerkleRoot()))
        throw new ProtocolException("Payload hash not equal to merkle root.");
    }

    public byte[] ComputeMerkleRoot()
    {
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

    public void Serialize(Stream stream)
    {
      byte[] bufferHeader = Header.Serialize();
      stream.Write(bufferHeader, 0, bufferHeader.Length);

      byte[] countTXs = VarInt.GetBytes(TXs.Count);
      stream.Write(countTXs, 0, countTXs.Length);

      TXs.ForEach(t => t.WriteToStream(stream));
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
