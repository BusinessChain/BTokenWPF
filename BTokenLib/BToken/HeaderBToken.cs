using System;
using System.Linq;


namespace BTokenLib
{
  public class HeaderBToken : Header
  {
    public const int COUNT_HEADER_BYTES = 108;

    // Statt den aktuellen DB Hash, könnte auch der diesem Block vorangehende DB Hash
    // aufgeführt werden. Dies hätte beim Mining der vorteil, dass das Inserten des 
    // gemineden Block nicht durchgespielt werden müsste.

    public byte[] HashDatabase = new byte[32];


    public HeaderBToken()
    {
      Difficulty = 1;
    }

    public HeaderBToken(
      byte[] headerHash,
      byte[] hashPrevious,
      byte[] merkleRootHash,
      byte[] hashDatabase,
      uint nonce) : base(
        headerHash,
        hashPrevious,
        merkleRootHash,
        nonce)
    {
      Difficulty = 1;
      HashDatabase = hashDatabase;
    }

    public override byte[] Serialize()
    {
      byte[] buffer = new byte[COUNT_HEADER_BYTES];

      HashPrevious.CopyTo(buffer, 0);

      MerkleRoot.CopyTo(buffer, 32);

      HashDatabase.CopyTo(buffer, 64);

      BitConverter.GetBytes(UnixTimeSeconds).CopyTo(buffer, 96);

      BitConverter.GetBytes(Nonce).CopyTo(buffer, 100);

      return buffer;
    }

    public override void AppendToHeader(Header headerPrevious)
    {
      if (headerPrevious.HeaderParent != null)
      {
        Header headerParent = headerPrevious.HeaderParent.HeaderNext;

        while (true)
        {
          if (headerParent == null)
            throw new ProtocolException($"Cannot append header {this} to header {headerPrevious} because it is not anchored in parent chain.");

          if (headerParent.HashesChild.Any(h => h.Value.IsAllBytesEqual(Hash)))
          {
            HeaderParent = headerParent;
            break;
          }

          headerParent = headerParent.HeaderNext;
        }
      }

      base.AppendToHeader(headerPrevious);
    }
  }
}
