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


    private void ModifyFileAtomic(string pathOriginal, Action<string> modifyTempFile)
    {
      string pathTemp = pathOriginal + ".tmp";

      File.Copy(pathOriginal, pathTemp, overwrite: true);

      modifyTempFile(pathTemp);

      File.Move(pathTemp, pathOriginal, overwrite: true);
    }

    public void WriteToDiskAtomic(string pathFileHeaderchain)
    {
      string pathTemp = pathFileHeaderchain + ".tmp";

      File.Copy(pathFileHeaderchain, pathTemp, overwrite: true);

      using (FileStream fileStream = new(pathTemp, FileMode.Append, FileAccess.Write))
      {
        int positionStartHeader = (int)fileStream.Position;

        fileStream.Write(Serialize());

        fileStream.Write(BitConverter.GetBytes(CountBytesTXs));

        fileStream.Write(VarInt.GetBytes(HashesChild.Count));

        foreach (var hashChild in HashesChild)
        {
          fileStream.Write(hashChild.Key);
          fileStream.Write(hashChild.Value);
        }

        fileStream.Write(BitConverter.GetBytes(positionStartHeader));
      }

      File.Move(pathTemp, pathFileHeaderchain, overwrite: true);
    }

    public void ReverseHeaderOnDiskAtomic(string pathFileHeaderchain)
    {
      string pathTemp = pathFileHeaderchain + ".tmp";

      File.Copy(pathFileHeaderchain, pathTemp, overwrite: true);

      using (FileStream stream = new FileStream(pathTemp, FileMode.Open, FileAccess.ReadWrite))
      {
        stream.Seek(-4, SeekOrigin.End);

        byte[] buffer = new byte[4];
        stream.Read(buffer, 0, 4);

        int positionStartHeader = BitConverter.ToInt32(buffer, 0);
        stream.SetLength(positionStartHeader);
      }

      File.Move(pathTemp, pathFileHeaderchain, overwrite: true);
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
