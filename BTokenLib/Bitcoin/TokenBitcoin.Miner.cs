using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace BTokenLib
{
  partial class TokenBitcoin : Token
  {
    const long BLOCK_REWARD_INITIAL = 5000000000;
    const int PERIOD_HALVENING_BLOCK_REWARD = 210000;

    const int COUNT_TXS_PER_BLOCK_MAX = 5;
    int NumberOfProcesses = Math.Max(Environment.ProcessorCount - 1, 1);


    protected override void RunMining()
    {
      Parallel.For(
        0,
        NumberOfProcesses,
        i => RunMiningProcess(i));

      "Exit Bitcoin miner.".Log(this, LogFile, LogEntryNotifier);
    }

    void RunMiningProcess(int indexThread)
    {
      $"Start {GetName()} miner on thread {indexThread}."
        .Log(this, LogFile, LogEntryNotifier);

      SHA256 sHA256 = SHA256.Create();

      while (true)
      {
        BlockBitcoin block = ComputePoW(sHA256, indexThread);

        if (!IsMining)
          return;

        while (!TryLock())
          Thread.Sleep(500);

        try
        {
          InsertBlock(block);

          Console.Beep();

          ($"Bitcoin Miner {indexThread} mined block height " +
            $"{block.Header.Height} with hash {block}.")
            .Log(this, LogFile, LogEntryNotifier);

          Network.AdvertizeBlockToNetwork(block);
        }
        catch (Exception ex)
        {
          ($"{ex.GetType().Name} when when miner tries to insert mined bitcoin " +
            $"block height {block.Header.Height}, {block}:\n{ex.Message}.")
            .Log(this, LogFile, LogEntryNotifier);

          continue;
        }
        finally
        {
          ReleaseLock();
        }
      }
    }

    BlockBitcoin ComputePoW(SHA256 sHA256, int indexThread)
    {
    LABEL_StartPoW:

      uint seed = (uint)(indexThread * uint.MaxValue / NumberOfProcesses);

      BlockBitcoin block = new(this);

      block.TXs.AddRange(TXPool.GetTXs(COUNT_TXS_PER_BLOCK_MAX));

      int height = HeaderTip.Height + 1;

      long blockReward = BLOCK_REWARD_INITIAL >> height / PERIOD_HALVENING_BLOCK_REWARD;

      block.TXs.Insert(0, CreateCoinbaseTX(height, blockReward, sHA256));

      uint nBits = HeaderBitcoin.GetNextTarget((HeaderBitcoin)HeaderTip);
      double difficulty = HeaderBitcoin.ComputeDifficultyFromNBits(nBits);

      HeaderBitcoin header = new()
      {
        Version = 0x01,
        HashPrevious = HeaderTip.Hash,
        HeaderPrevious = HeaderTip,
        Height = height,
        UnixTimeSeconds = (uint)DateTimeOffset.Now.ToUnixTimeSeconds(),
        Nonce = seed,
        NBits = nBits,
        Difficulty = difficulty,
        DifficultyAccumulated = HeaderTip.DifficultyAccumulated + difficulty,
        MerkleRoot = block.ComputeMerkleRoot(),
        CountBytesTXs = HeaderBitcoin.COUNT_HEADER_BYTES
      };

      header.CountBytesTXs += block.TXs.Sum(t => t.TXRaw.Count);

      block.Header = header;

      header.ComputeHash(sHA256);

      while (header.Hash.IsGreaterThan(header.NBits))
      {
        if (HeaderTip.Height >= height || TXPool.GetFlagTXAddedSinceLastInquiry())
          goto LABEL_StartPoW;

        if (!IsMining)
          break;

        header.IncrementNonce(seed);
        header.ComputeHash(sHA256);
      }

      return block;
    }

    public TX CreateCoinbaseTX(int height, long blockReward, SHA256 sHA256)
    {
      List<byte> tXRaw = new();

      tXRaw.AddRange(new byte[4] { 0x01, 0x00, 0x00, 0x00 }); // version

      tXRaw.Add(0x01); // #TxIn

      tXRaw.AddRange(new byte[32]); // TxOutHash

      tXRaw.AddRange("FFFFFFFF".ToBinary()); // TxOutIndex

      byte[] blockHeight = VarInt.GetBytes(height); // Script coinbase
      tXRaw.Add((byte)blockHeight.Length);
      tXRaw.AddRange(blockHeight);

      tXRaw.AddRange("FFFFFFFF".ToBinary()); // sequence

      tXRaw.Add(0x01); // #TxOut

      tXRaw.AddRange(BitConverter.GetBytes(blockReward));

      WalletBitcoin wallet = (WalletBitcoin)Wallet;
      tXRaw.Add((byte)wallet.PublicScript.Length);
      tXRaw.AddRange(wallet.PublicScript);

      tXRaw.AddRange(new byte[4]);

      MemoryStream stream = new(tXRaw.ToArray());

      return ParseTX(stream, sHA256);
    }
  }
}
