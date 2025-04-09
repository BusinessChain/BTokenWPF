using System;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using System.Security.Cryptography;



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
    public Dictionary<byte[], byte[]> HashesChild = new(new EqualityComparerByteArray());

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

    public void WriteToDisk(FileStream fileStream)
    {
      fileStream.Write(Serialize());

      fileStream.Write(BitConverter.GetBytes(CountBytesTXs));

      fileStream.Write(VarInt.GetBytes(HashesChild.Count));

      foreach (var hashChild in HashesChild)
      {
        fileStream.Write(hashChild.Key);
        fileStream.Write(hashChild.Value);
      }
    }

    public virtual void AppendToHeader(Header headerPrevious)
    {
      Height = headerPrevious.Height + 1;

      if (!HashPrevious.IsAllBytesEqual(headerPrevious.Hash))
        throw new ProtocolException($"Header {this} references header previous {HashPrevious.ToHexString()} but attempts to append to {headerPrevious}.");

      HeaderPrevious = headerPrevious;
      DifficultyAccumulated = headerPrevious.DifficultyAccumulated + Difficulty;
      CountBytesTXsAccumulated = headerPrevious.CountBytesTXsAccumulated + CountBytesTXs;

      if (headerPrevious.HeaderParent != null)
      {
        Header headerParent = headerPrevious.HeaderParent.HeaderNext;

        while (true)
        {
          if (headerParent == null)
            throw new ProtocolException($"Cannot append header {this} to header {headerPrevious} because it is not anchored in parent chain.");

          if(headerParent.HashesChild.Any(h => h.Value.IsAllBytesEqual(Hash)))
          {
            HeaderParent = headerParent;
            break;
          }

          headerParent = headerParent.HeaderNext;
        }
      }
    }

    public void ComputeHash()
    {
      SHA256 sHA256 = SHA256.Create();
      ComputeHash(sHA256);
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
