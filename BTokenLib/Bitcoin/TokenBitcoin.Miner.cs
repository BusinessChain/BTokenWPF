using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Security.Cryptography;


namespace BTokenLib
{
  partial class TokenBitcoin : Token
  {
    const int COUNT_TXS_PER_BLOCK_MAX = 3;
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
        Block block = ComputePoW(sHA256, indexThread);

        if (!IsMining)
          return;

        while (!TryLock())
          Thread.Sleep(100);

        try
        {
          $"Bitcoin Miner {indexThread} mined block height {block.Header.Height} with hash {block}.\n\n"
            .Log(this, LogFile, LogEntryNotifier);

          InsertBlock(block);

          Console.Beep();

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

    Block ComputePoW(SHA256 sHA256, int indexThread)
    {
    LABEL_StartPoW:

      int height = Network.HeaderTip.Height + 1;

      Block block = new(this);

      block.TXs.Add(CreateCoinbaseTX(height, sHA256));
      block.TXs.AddRange(TXPool.GetTXs(COUNT_TXS_PER_BLOCK_MAX, out long feeTXs));

      uint nBits = HeaderBitcoin.GetNextTarget((HeaderBitcoin)Network.HeaderTip);
      double difficulty = HeaderBitcoin.ComputeDifficultyFromNBits(nBits);
      uint seed = (uint)(indexThread * uint.MaxValue / NumberOfProcesses);

      HeaderBitcoin header = new()
      {
        Version = 0x01,
        HashPrevious = Network.HeaderTip.Hash,
        HeaderPrevious = Network.HeaderTip,
        Height = height,
        UnixTimeSeconds = (uint)DateTimeOffset.Now.ToUnixTimeSeconds(),
        Nonce = seed,
        NBits = nBits,
        Difficulty = difficulty,
        DifficultyAccumulated = Network.HeaderTip.DifficultyAccumulated + difficulty,
        MerkleRoot = block.ComputeMerkleRoot()
      };

      header.CountBytesTXs += block.TXs.Sum(t => t.TXRaw.Length);

      header.ComputeHash(sHA256);

      while (block.Header.Hash.IsGreaterThan(header.NBits))
      {
        if (Network.HeaderTip.Height >= height || ((PoolTXBitcoin)TXPool).GetFlagTXAddedSinceLastInquiry())
          goto LABEL_StartPoW;

        if (!IsMining)
          break;

        header.IncrementNonce(seed);
        header.ComputeHash(sHA256);
      }

      block.Header = header;
      block.Serialize();

      return block;
    }

    public TX CreateCoinbaseTX(int height, SHA256 sHA256)
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

      long blockReward = BLOCK_REWARD_INITIAL >> height / PERIOD_HALVENING_BLOCK_REWARD;
      tXRaw.AddRange(BitConverter.GetBytes(blockReward));

      WalletBitcoin wallet = (WalletBitcoin)Wallet;
      tXRaw.Add((byte)wallet.PublicScript.Length);
      tXRaw.AddRange(wallet.PublicScript);

      tXRaw.AddRange(new byte[4]);

      return ParseTX(tXRaw.ToArray(), sHA256);
    }
  }
}
