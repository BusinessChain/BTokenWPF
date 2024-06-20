﻿using System.Security.Cryptography;



namespace BTokenLib
{
  public abstract class Header
  {
    public byte[] Hash;
    public byte[] HashPrevious;
    public byte[] MerkleRoot;
    public uint UnixTimeSeconds;
    public uint Nonce;

    public Header HeaderPrevious;
    public Header HeaderNext;

    public Header HeaderParent;
    public byte[] HashChild;

    public int Height;
    public int CountTXs;

    public int CountBytesTXs;
    public long CountBytesTXsAccumulated;

    public double Difficulty;
    public double DifficultyAccumulated;

    public long Fee;
    public double FeePerByte;


    public Header()
    {
      Hash = new byte[32];
      HashPrevious = new byte[32];
      MerkleRoot = new byte[32];
    }

    public Header(
      byte[] headerHash,
      byte[] hashPrevious,
      byte[] merkleRootHash,
      uint unixTimeSeconds,
      uint nonce)
    {
      Hash = headerHash;
      HashPrevious = hashPrevious;
      MerkleRoot = merkleRootHash;
      UnixTimeSeconds = unixTimeSeconds;
      Nonce = nonce;
    }

    public abstract byte[] Serialize();

    public virtual void AppendToHeader(Header headerPrevious)
    {
      Height = headerPrevious.Height + 1;

      HeaderPrevious = headerPrevious;

      DifficultyAccumulated = headerPrevious.DifficultyAccumulated + Difficulty;
      CountBytesTXsAccumulated = headerPrevious.CountBytesTXsAccumulated + CountBytesTXs;

      if (!HashPrevious.IsAllBytesEqual(headerPrevious.Hash))
        throw new ProtocolException(
          $"Header {this} references header previous " +
          $"{HashPrevious.ToHexString()} but attempts to append to {headerPrevious}.");

      if (headerPrevious.HeaderParent != null)
      {
        Header headerParent = headerPrevious.HeaderParent.HeaderNext;

        while (true)
        {
          if (headerParent == null)
            throw new ProtocolException($"Header {this} not anchored in parent chain.");

          if(headerParent.HashChild != null && headerParent.HashChild.IsAllBytesEqual(Hash))
          {
            HeaderParent = headerParent;
            return;
          }

          headerParent = headerParent.HeaderNext;
        }
      }
    }

    public void ComputeHash(SHA256 sHA256)
    {
      Hash = sHA256.ComputeHash(
        sHA256.ComputeHash(Serialize()));
    }

   
    public override string ToString()
    {
      return Hash.ToHexString();
    }
  }
}
