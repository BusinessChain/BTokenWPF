using System;
using System.IO;
using System.Linq;
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

    public static byte[] IDENTIFIER_BTOKEN_PROTOCOL = new byte[] { (byte)'B', (byte)'T' };


    protected override async void RunMining()
    {
      $"Miner starts.".Log(this, LogFile, LogEntryNotifier);

      Header headerTipParent = null;
      Header headerTip = null;

      int timerMinerPause = 0;
      int timerCreateNextToken = 0;
      int timeMinerLoopMilliseconds = 100;

      FeeSatoshiPerByteAnchorToken = TokenParent.Network.HeaderTip.FeePerByte;

      if (FeeSatoshiPerByteAnchorToken < MINIMUM_FEE_SATOSHI_PER_BYTE_ANCHOR_TOKEN)
        FeeSatoshiPerByteAnchorToken = MINIMUM_FEE_SATOSHI_PER_BYTE_ANCHOR_TOKEN;

      while (IsMining)
      {
        if (TryLock())
        {
          if (headerTip == null)
          {
            headerTip = Network.HeaderTip;
            headerTipParent = TokenParent.Network.HeaderTip;
          }

          if (headerTipParent != TokenParent.Network.HeaderTip)
          {
            timerMinerPause = TIME_MINER_PAUSE_AFTER_RECEIVE_PARENT_BLOCK_SECONDS * 1000
              / timeMinerLoopMilliseconds;

            headerTipParent = TokenParent.Network.HeaderTip;
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

              BlocksMinedCache.Add(block);

              block.WriteToDisk(Path.Combine(PathBlocksMined, block.Header.Hash.ToHexString()));
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

      int height = Network.HeaderTip.Height + 1;

      long blockReward = BLOCK_REWARD_INITIAL >> height / PERIOD_HALVENING_BLOCK_REWARD;
      blockReward += feeTXs;

      TX tXCoinbase = ((WalletBToken)Wallet).CreateTXCoinbase(blockReward, height);

      block.TXs.Insert(0, tXCoinbase);

      block.Header = new HeaderBToken()
      {
        HashPrevious = Network.HeaderTip.Hash,
        HeaderPrevious = Network.HeaderTip,
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
    
    public override void InsertBlockMined(byte[] hashBlock)
    {
      try
      {
        Block blockMined = BlocksMinedCache.Find(b => b.Header.Hash.IsAllBytesEqual(hashBlock));

        if (blockMined == null)
        {
          blockMined = new(this, File.ReadAllBytes(Path.Combine(PathBlocksMined, hashBlock.ToHexString())));
          blockMined.Parse(Network.HeaderTip.Height + 1);
        }

        InsertBlock(blockMined);
      }
      catch (Exception ex)
      {
        $"{ex.GetType().Name} when attempting to load mined block {hashBlock.ToHexString()}: {ex.Message}.\n".Log(this, LogEntryNotifier);
      }
    }
  }
}
