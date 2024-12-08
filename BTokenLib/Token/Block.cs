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
    public List<TX> TXs = new();

    public byte[] Buffer;
    public int LengthBufferPayload;

    public SHA256 SHA256 = SHA256.Create();


    public Block(Token token) 
      : this(token, new byte[token.SizeBlockMax])
    { }

    public Block(Token token, byte[] buffer)
    {
      Token = token;
      Buffer = buffer;
    }

    public void Parse(int heightBlock)
    {
      Dictionary<byte[], byte[]> biggestDifferencesTemp = new(new EqualityComparerByteArray());
      Dictionary<byte[], TX> tXAnchorWinners = new(new EqualityComparerByteArray());

      TXs.Clear();
      int startIndex = 0;

      Header = Token.ParseHeader(Buffer, ref startIndex, SHA256);

      int tXCount = VarInt.GetInt(Buffer, ref startIndex);

      if (tXCount == 0)
        throw new ProtocolException($"Block {this} lacks coinbase transaction.");

      int startIndexBeginningOfTXs = startIndex;

      long blockReward = Token.BlockRewardInitial >> Header.Height / Token.PeriodHalveningBlockReward;

      TXs.Add(Token.ParseTXCoinbase(Buffer, ref startIndex, SHA256, blockReward));

      for (int t = 1; t < tXCount; t++)
      {
        TX tX = Token.ParseTX(Buffer, ref startIndex, SHA256);
        TXs.Add(tX);

        if (tX.TryGetAnchorToken(out TokenAnchor tokenAnchor))
        {
          byte[] differenceHash = SHA256.HashData(Header.Hash).SubtractByteWise(tX.Hash);

          if (tXAnchorWinners.TryGetValue(tokenAnchor.IDToken, out TX tXAnchorWinner))
          {
            bool flagIsGreaterThan = differenceHash.IsGreaterThan(biggestDifferencesTemp[tokenAnchor.IDToken]);

            if (flagIsGreaterThan || tX.IsSuccessorTo(tXAnchorWinner))
            {
              tXAnchorWinners[tokenAnchor.IDToken] = tX;
              Header.HashesChild[tokenAnchor.IDToken] = tokenAnchor.HashBlockReferenced;

              if (flagIsGreaterThan)
                biggestDifferencesTemp[tokenAnchor.IDToken] = differenceHash;
            }
          }
          else
          {
            tXAnchorWinners[tokenAnchor.IDToken] = tX;
            biggestDifferencesTemp[tokenAnchor.IDToken] = differenceHash;
          }
        }
      }

      if (!Header.MerkleRoot.IsAllBytesEqual(ComputeMerkleRoot()))
        throw new ProtocolException("Header merkle root not equal to computed transactions merkle root.");

      Header.CountTXs = TXs.Count;
      Header.CountBytesTXs = startIndex - startIndexBeginningOfTXs;
      Header.Fee = TXs.Sum(t => t.Fee);
      Header.FeePerByte = (double)Header.Fee / Header.CountBytesTXs;

      if (blockReward + Header.Fee != TXs[0].GetValueOutputs())
        throw new ProtocolException($"Output values of coinbase not equal to blockReward plus tx fees.");
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

          merkleList[i] = SHA256.ComputeHash(SHA256.ComputeHash(leafPair));
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
