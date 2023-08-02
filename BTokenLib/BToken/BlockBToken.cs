﻿using System;
using System.Linq;
using System.Text;
using System.Security.Cryptography;


namespace BTokenLib
{
  class BlockBToken : Block
  {
    public BlockBToken()
    {
      Header = new HeaderBToken();
    }

    public BlockBToken(int sizeBuffer)
    {
      Buffer = new byte[sizeBuffer];
    }


    public void Parse(byte[] buffer)
    {
      int indexBuffer = 0;
      // parse Anchor Token

      Parse(indexBuffer);
    }

    public override HeaderBToken ParseHeader(
      byte[] buffer,
      ref int index)
    {
      byte[] hash =
        SHA256.ComputeHash(
          SHA256.ComputeHash(
            buffer,
            index,
            HeaderBToken.COUNT_HEADER_BYTES));

      byte[] hashHeaderPrevious = new byte[32];
      Array.Copy(buffer, index, hashHeaderPrevious, 0, 32);
      index += 32;

      byte[] merkleRootHash = new byte[32];
      Array.Copy(buffer, index, merkleRootHash, 0, 32);
      index += 32;

      byte[] hashAnchorPrevious = new byte[32];
      Array.Copy(buffer, index, hashAnchorPrevious, 0, 32);
      index += 32;

      uint unixTimeSeconds = BitConverter.ToUInt32(
        buffer, index);
      index += 4;

      uint nonce = BitConverter.ToUInt32(buffer, index);
      index += 4;

      return new HeaderBToken(
        hash,
        hashHeaderPrevious,
        merkleRootHash,
        unixTimeSeconds,
        nonce);
    }
  }
}
