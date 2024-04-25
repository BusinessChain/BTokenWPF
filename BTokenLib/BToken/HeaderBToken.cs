using System;

namespace BTokenLib
{
  class HeaderBToken : Header
  {
    public const int COUNT_HEADER_BYTES = 104;

    // Statt den aktuellen DB Hash, könnte auch der diesem Block vorangehende DB Hash
    // aufgeführt werden. Dies hätte beim Mining der vorteil, dass das Inserten des 
    // gemineden Block nicht durchgespielt werden müsste.

    public byte[] HashDatabase = new byte[32];
    public int HeightAnchorPrevious;

    static uint InitializerNonce;



    public HeaderBToken()
    {
      Nonce = InitializerNonce++;

      Difficulty = 1;
    }

    public HeaderBToken(
      byte[] headerHash,
      byte[] hashPrevious,
      byte[] merkleRootHash,
      uint unixTimeSeconds,
      uint nonce) : base(
        headerHash,
        hashPrevious,
        merkleRootHash,
        unixTimeSeconds,
        nonce)
    {
      Difficulty = 1;
    }

    public override byte[] Serialize()
    {
      byte[] buffer = new byte[COUNT_HEADER_BYTES];

      HashPrevious.CopyTo(buffer, 0);

      MerkleRoot.CopyTo(buffer, 32);

      HashDatabase.CopyTo(buffer, 64);

      BitConverter.GetBytes(UnixTimeSeconds)
        .CopyTo(buffer, 96);

      BitConverter.GetBytes(Nonce)
        .CopyTo(buffer, 100);

      return buffer;
    }
  }
}
