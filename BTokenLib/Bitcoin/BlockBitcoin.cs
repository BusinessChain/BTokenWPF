using System;
using System.IO;
using System.Security.Cryptography;


namespace BTokenLib
{
  class BlockBitcoin : Block
  {
    const long VALUE_REWARD_INITIAL_SATOSHI = 5000000000;
    const int NUMBER_OF_BLOCKS_HALFING_CYCLE = 210000;


    public BlockBitcoin(Token token) 
      : base(token)
    { }

    public override HeaderBitcoin ParseHeader(Stream stream)
    {
      byte[] buffer = new byte[HeaderBitcoin.COUNT_HEADER_BYTES];
      stream.Read(buffer, 0, buffer.Length);

      int index = 0;

      return ParseHeader(buffer, ref index);
    }

    public override HeaderBitcoin ParseHeader(
      byte[] buffer,
      ref int index)
    {
      byte[] hash =
        SHA256.ComputeHash(
          SHA256.ComputeHash(
            buffer,
            index,
            HeaderBitcoin.COUNT_HEADER_BYTES));

      uint version = BitConverter.ToUInt32(buffer, index);
      index += 4;

      byte[] previousHeaderHash = new byte[32];
      Array.Copy(buffer, index, previousHeaderHash, 0, 32);
      index += 32;

      byte[] merkleRootHash = new byte[32];
      Array.Copy(buffer, index, merkleRootHash, 0, 32);
      index += 32;

      uint unixTimeSeconds = BitConverter.ToUInt32(
        buffer, index);
      index += 4;

      bool isBlockTimePremature = unixTimeSeconds >
        (DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 2 * 60 * 60);
      
      if (isBlockTimePremature)
        throw new ProtocolException(
          $"Timestamp premature {new DateTime(unixTimeSeconds).Date}.");

      uint nBits = BitConverter.ToUInt32(buffer, index);
      index += 4;

      if (hash.IsGreaterThan(nBits))
        throw new ProtocolException(
          $"Header hash {hash.ToHexString()} greater than NBits {nBits}.");

      uint nonce = BitConverter.ToUInt32(buffer, index);
      index += 4;

      return new HeaderBitcoin(
        hash,
        version,
        previousHeaderHash,
        merkleRootHash,
        unixTimeSeconds,
        nBits,
        nonce);
    }
  }
}
