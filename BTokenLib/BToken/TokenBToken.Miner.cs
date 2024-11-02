using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;


namespace BTokenLib
{
  public partial class TokenBToken : Token
  {
    const int COUNT_BYTES_PER_BLOCK_MAX = 1000;
    const int TIMESPAN_MINING_ANCHOR_TOKENS_SECONDS = 4;
    const int TIME_MINER_PAUSE_AFTER_RECEIVE_PARENT_BLOCK_SECONDS = 5;
    const double FACTOR_INCREMENT_FEE_PER_BYTE_ANCHOR_TOKEN = 1.02;
    const double MINIMUM_FEE_SATOSHI_PER_BYTE_ANCHOR_TOKEN = 0.1;

    double FeeSatoshiPerByteAnchorToken;
    List<Block> BlocksMinedCache = new();
    string PathBlocksMined;


    protected override async void RunMining()
    {
      $"Miner {this} starts.".Log(this, LogFile, LogEntryNotifier);

      Header headerTipParent = null;
      Header headerTip = null;

      int timerMinerPause = 0;
      int timerCreateNextToken = 0;
      int timeMinerLoopMilliseconds = 100;

      FeeSatoshiPerByteAnchorToken = TokenParent.HeaderTip.FeePerByte;

      if (FeeSatoshiPerByteAnchorToken < MINIMUM_FEE_SATOSHI_PER_BYTE_ANCHOR_TOKEN)
        FeeSatoshiPerByteAnchorToken = MINIMUM_FEE_SATOSHI_PER_BYTE_ANCHOR_TOKEN;

      while (IsMining)
      {
        if (TryLock())
        {
          if (headerTip == null)
          {
            headerTip = HeaderTip;
            headerTipParent = TokenParent.HeaderTip;
          }

          if (headerTipParent != TokenParent.HeaderTip)
          {
            timerMinerPause = TIME_MINER_PAUSE_AFTER_RECEIVE_PARENT_BLOCK_SECONDS * 1000
              / timeMinerLoopMilliseconds;

            headerTipParent = TokenParent.HeaderTip;
          }

          if (timerMinerPause > 0)
            timerMinerPause -= 1;

          if (timerCreateNextToken > 0)
            timerCreateNextToken -= 1;

          if (timerMinerPause == 0 && timerCreateNextToken == 0)
          {
            timerCreateNextToken = TIMESPAN_MINING_ANCHOR_TOKENS_SECONDS * 1000
              / timeMinerLoopMilliseconds;

            byte[] dataAnchorToken = CreateAnchorToken(out Block block);

            if (TokenParent.TryBroadcastTXData(dataAnchorToken, FeeSatoshiPerByteAnchorToken))
            {
              $"Mine block {block}.".Log(this, LogFile, LogEntryNotifier);
              SaveBlockMined(block);
            }
          }

          ReleaseLock();
        }

        await Task.Delay(timeMinerLoopMilliseconds).ConfigureAwait(false);
      }

      $"Exit BToken miner.".Log(this, LogFile, LogEntryNotifier);
    }

    byte[] CreateAnchorToken(out Block block)
    {
      block = new Block(this);

      block.TXs.AddRange(TXPool.GetTXs(COUNT_BYTES_PER_BLOCK_MAX, out long feeTXs));

      int height = HeaderTip.Height + 1;

      long blockReward = BLOCK_REWARD_INITIAL >> height / PERIOD_HALVENING_BLOCK_REWARD;
      blockReward += feeTXs;

      TX tXCoinbase = ((WalletBToken)Wallet).CreateTXCoinbase(blockReward, height);

      block.TXs.Insert(0, tXCoinbase);

      block.Header = new HeaderBToken()
      {
        HashPrevious = HeaderTip.Hash,
        HeaderPrevious = HeaderTip,
        Height = height,
        UnixTimeSeconds = (uint)DateTimeOffset.Now.ToUnixTimeSeconds(),
        MerkleRoot = block.ComputeMerkleRoot(),
        CountTXs = block.TXs.Count,
        Fee = feeTXs
      };

      block.Header.ComputeHash();

      block.Serialize();

      return IDENTIFIER_BTOKEN_PROTOCOL
      .Concat(IDToken)
      .Concat(block.Header.Hash)
      .Concat(block.Header.HashPrevious).ToArray();
    }

    void SaveBlockMined(Block block)
    {
      BlocksMinedCache.Add(block);

      string pathBlockMined = Path.Combine(PathBlocksMined, block.Header.Hash.ToHexString());

      while (true)
        try
        {
          using (FileStream fileStreamBlock = new(
          pathBlockMined,
          FileMode.Create,
          FileAccess.Write,
          FileShare.None))
          {
            block.WriteToStream(fileStreamBlock);
          }

          return;
        }
        catch (Exception ex)
        {
          ($"{ex.GetType().Name} when writing block height {block.Header.Height} to file:\n" +
            $"{ex.Message}\n " +
            $"Try again in {TIMEOUT_FILE_RELOAD_SECONDS} seconds ...").Log(this, LogEntryNotifier);

          Thread.Sleep(TIMEOUT_FILE_RELOAD_SECONDS);
        }
    }


    public override void DeleteBlocksMinedUnconfirmed()
    {
      BlocksMinedCache.Clear();

      foreach (string pathFile in Directory.GetFiles(PathBlocksMined))
        File.Delete(pathFile);
    }

    public override bool TryGetBlockMined(byte[] hashBlock, out Block blockMined)
    {
      blockMined = BlocksMinedCache.Find(b => b.Header.Hash.IsAllBytesEqual(hashBlock));

      if (blockMined == null)
      {
        string pathBlockMined = Path.Combine(PathBlocksMined, hashBlock.ToHexString());

        try
        {
          blockMined = ParseBlock(File.ReadAllBytes(pathBlockMined));
        }
        catch (Exception ex)
        {
          $"{ex.GetType().Name} when attempting to load block {pathBlockMined}: {ex.Message}.\n".Log(this, LogEntryNotifier);
        }
      }

      return blockMined != null;
    }
  }
}
