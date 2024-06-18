﻿using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace BTokenLib
{
  public abstract class Block
  {
    Token Token;

    public Header Header;

    public SHA256 SHA256 = SHA256.Create();

    public List<TX> TXs = new();


    public Block(Token token)
    {
      Token = token;
    }

    public void ParseTXs(Stream stream)
    {
      long positionStreamStart = stream.Position;

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

        byte[] targetValue = SHA256.ComputeHash(Header.Hash);
        byte[] biggestDifferenceTemp = new byte[32];

        TX tXWinner = null;

        for (int t = 0; t < tXCount; t += 1)
        {
          TX tX = Token.ParseTX(stream, SHA256);

          TXs.Add(tX);

          merkleList[t] = tX.Hash;

          if (tX.TryGetAnchorToken(out TokenAnchor tokenAnchor))
          {
            byte[] differenceHash = targetValue.SubtractByteWise(tX.Hash);

            if (differenceHash.IsGreaterThan(biggestDifferenceTemp) || tX.IsSuccessorTo(tXWinner))
            {
              biggestDifferenceTemp = differenceHash;
              tXWinner = tX;
              Header.HashChild = tokenAnchor.HashBlockReferenced;
            }
          }
        }

        if (tXsLengthMod2 != 0)
          merkleList[tXCount] = merkleList[tXCount - 1];
      }

      if (!Header.MerkleRoot.IsAllBytesEqual(ComputeMerkleRoot()))
        throw new ProtocolException("Payload hash not equal to merkle root.");

      Header.CountTXs = TXs.Count;
      Header.CountBytesTXs = (int)(stream.Position - positionStreamStart);

      DetermineAnchorTokenWinner();
    }

    bool DetermineAnchorTokenWinner()
    {
      byte[] targetValue = SHA256.ComputeHash(Header.Hash);
      byte[] biggestDifferenceTemp = new byte[32];

      TX tXWinner = null;

      foreach (TX tX in TXs)
      {
        if (!tX.TryGetAnchorToken(out TokenAnchor tokenAnchor))
          continue;

        byte[] differenceHash = targetValue.SubtractByteWise(tX.Hash);

        if (differenceHash.IsGreaterThan(biggestDifferenceTemp) || tX.IsSuccessorTo(tXWinner))
        {
          biggestDifferenceTemp = differenceHash;
          tXWinner = tX;
          Header.HashChild = tokenAnchor.HashBlockReferenced;
        }
      }

      return true;
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
