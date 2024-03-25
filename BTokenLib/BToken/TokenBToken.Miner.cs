using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using System.Security.Cryptography;

namespace BTokenLib
{
  partial class TokenBToken : Token
  {
    const int COUNT_TXS_PER_BLOCK_MAX = 5;
    const int TIMESPAN_MINING_ANCHOR_TOKENS_SECONDS = 10;
    const int TIME_MINER_PAUSE_AFTER_RECEIVE_PARENT_BLOCK_SECONDS = 10;
    const double FACTOR_INCREMENT_FEE_PER_BYTE = 1.2;

    string PathBlocksMinedUnconfirmed;
    List<BlockBToken> BlocksMined = new();

    SHA256 SHA256Miner = SHA256.Create();
    Random RandomGeneratorMiner = new();
    int NumberSequence;

    List<TokenAnchor> TokensAnchorUnconfirmed = new();
    List<TokenAnchor> TokensAnchorDetectedInBlock = new();


    protected override async void RunMining()
    {
      $"Miners starts with fee per byte = {FeeSatoshiPerByte}".Log(this, LogFile, LogEntryNotifier);

      Header headerTipParent = null;
      Header headerTip = null;

      int timerMinerPause = 0;
      int timerCreateNextToken = 0;
      int timeMinerLoopMilliseconds = 100;

      while (IsMining)
      {
        if (TryLock())
        {
          if (headerTip == null)
          {
            headerTip = HeaderTip;
            headerTipParent = TokenParent.HeaderTip;
          }

          if(headerTipParent != TokenParent.HeaderTip)
          {
            timerMinerPause = TIME_MINER_PAUSE_AFTER_RECEIVE_PARENT_BLOCK_SECONDS * 1000 
              / timeMinerLoopMilliseconds;

            headerTipParent = TokenParent.HeaderTip;
          }

          if(headerTip != HeaderTip)
          {
            if (TokensAnchorUnconfirmed.Count == 0)
            {
              if (BlocksMined.Count > 0)
                FeeSatoshiPerByte /= FACTOR_INCREMENT_FEE_PER_BYTE;

              NumberSequence = 0;

              BlocksMined.Clear();
            }
            else
            {
              FeeSatoshiPerByte *= FACTOR_INCREMENT_FEE_PER_BYTE;
              NumberSequence += 1;

              RBFAnchorTokens();
            }

            headerTip = HeaderTip;
            timerMinerPause = 0;
          }

          if (timerMinerPause > 0)
            timerMinerPause -= 1;

          if (timerCreateNextToken > 0)
            timerCreateNextToken -= 1;

          if (timerMinerPause == 0 && timerCreateNextToken == 0)
            if (TryMineAnchorToken(out TokenAnchor tokenAnchor))
            {
              timerCreateNextToken = TIMESPAN_MINING_ANCHOR_TOKENS_SECONDS * 1000 
                / timeMinerLoopMilliseconds;

              TokensAnchorUnconfirmed.Add(tokenAnchor);

              TokenParent.BroadcastAnchorToken(tokenAnchor);

              $"{TokensAnchorUnconfirmed.Count} mined unconfirmed anchor tokens referencing block {tokenAnchor.HashBlockReferenced.ToHexString()}.".Log(this, LogFile, LogEntryNotifier);

              // timeMSLoop = (int)(tokenAnchor.TX.Fee * TIMESPAN_DAY_SECONDS * 1000 /
              // COUNT_SATOSHIS_PER_DAY_MINING);

              // timeMSLoop = RandomGeneratorMiner.Next(
              // timeMSCreateNextAnchorToken / 2,
              // timeMSCreateNextAnchorToken * 3 / 2);
            }

          ReleaseLock();
        }

        await Task.Delay(timeMinerLoopMilliseconds).ConfigureAwait(false);
      }

      $"Exit BToken miner.".Log(this, LogFile, LogEntryNotifier);
    }

    public bool TryMineAnchorToken(out TokenAnchor tokenAnchor)
    {
      BlockBToken block = new(this);

      int height = HeaderTip.Height + 1;

      long blockReward = BLOCK_REWARD_INITIAL >>
        height / PERIOD_HALVENING_BLOCK_REWARD;

      TX tXCoinbase = Wallet.CreateCoinbaseTX(height, blockReward);

      block.TXs.Add(tXCoinbase);
      block.TXs.AddRange(TXPool.GetTXs(COUNT_TXS_PER_BLOCK_MAX)); // should be bytes per block

      HeaderBToken header = new()
      {
        HashPrevious = HeaderTip.Hash,
        HeaderPrevious = HeaderTip,
        Height = height,
        UnixTimeSeconds = (uint)DateTimeOffset.Now.ToUnixTimeSeconds(),
        MerkleRoot = block.ComputeMerkleRoot(),
        CountTXs = block.TXs.Count
      };

      block.Header = header;

      header.ComputeHash(SHA256Miner);

      block.Buffer = block.Header.Buffer.Concat(
        VarInt.GetBytes(block.TXs.Count)).ToArray();

      block.TXs.ForEach(t =>
      { block.Buffer = block.Buffer.Concat(t.TXRaw).ToArray(); });

      block.Header.CountBytesBlock = block.Buffer.Length;

      tokenAnchor = new();

      tokenAnchor.NumberSequence = NumberSequence;
      tokenAnchor.HashBlockReferenced = block.Header.Hash;
      tokenAnchor.HashBlockPreviousReferenced = block.Header.HashPrevious;
      tokenAnchor.IDToken = IDToken;
            
      string pathFileBlock = Path.Combine(
        PathBlocksMinedUnconfirmed, 
        block.ToString());

      // File.WriteAllBytes(pathFileBlock, block.Buffer);

      BlocksMined.Add(block);

      $"BToken miner successfully mined anchor Token {tokenAnchor}.".Log(this, LogFile, LogEntryNotifier);

      return true;
    }

    void RBFAnchorTokens()
    {
      TokensAnchorUnconfirmed.Reverse();

      foreach (TokenAnchor tokenAnchor in TokensAnchorUnconfirmed)
      {
        TokenParent.Wallet.ReverseTXUnconfirmed(tokenAnchor.TX);

        File.Delete(Path.Combine(
          PathBlocksMinedUnconfirmed,
          tokenAnchor.TX.Hash.ToHexString()));
      }

      int countTokensAnchorUnconfirmed = TokensAnchorUnconfirmed.Count;
      TokensAnchorUnconfirmed.Clear();

      $"RBF {countTokensAnchorUnconfirmed} anchorTokens".Log(this, LogFile, LogEntryNotifier);

      while (countTokensAnchorUnconfirmed-- > 0 && TryMineAnchorToken(out TokenAnchor tokenAnchor))
        TokensAnchorUnconfirmed.Add(tokenAnchor);

      TokenParent.BroadcastTX(TokensAnchorUnconfirmed.Select(t => t.TX).ToList());
    }

    public override void SignalParentBlockInsertion(
      Header headerAnchor,
      List<TokenAnchor> tokensAnchor)
    {
      foreach(TokenAnchor tokenAnchor in tokensAnchor)
      {
        if (TokensAnchorUnconfirmed.RemoveAll(t => t.TX.Hash.IsEqual(tokenAnchor.TX.Hash)) > 0)
          $"Detected self mined anchor token {tokenAnchor} in Bitcoin block.".Log(this, LogFile, LogEntryNotifier);
        else
          $"Detected foreign mined anchor token {tokenAnchor} in Bitcoin block.".Log(this, LogFile, LogEntryNotifier);

        TokensAnchorDetectedInBlock.Add(tokenAnchor);
      }

      Block block;

      try
      {
        if (TokensAnchorDetectedInBlock.Count > 0)
        {
          headerAnchor.HashChild = GetHashBlockChild(headerAnchor.Hash);

          TokensAnchorDetectedInBlock.Clear();

          if (BlocksMined.Count > 0)
          {
            block = BlocksMined.Find(b =>
            b.Header.Hash.IsEqual(headerAnchor.HashChild));

            if (block == null)
              return;

            if (block.Header.HashPrevious.IsEqual(HeaderTip.Hash))
              InsertBlock(block);
            else
              ($"Self mined block {block} is obsoleted.\n" +
                $"This may happen, if a miner in the anchor chain withholds a mined block,\n" +
                $"and releases it after mining a subsequent block which now contains anchor " +
                $"tokens referencing a block prior to the tip.").Log(this, LogFile, LogEntryNotifier);
          }
        }
      }
      catch (Exception ex)
      {
        block = null;

        ($"{ex.GetType().Name} when signaling Bitcoin block {headerAnchor}" +
          $" with height {headerAnchor.Height} to BToken:\n" +
          $"Exception message: {ex.Message}").Log(this, LogFile, LogEntryNotifier);
      }
    }

    byte[] GetHashBlockChild(byte[] hashHeaderAnchor)
    {
      SHA256 sHA256 = SHA256.Create();

      byte[] targetValue = sHA256.ComputeHash(hashHeaderAnchor);
      byte[] biggestDifferenceTemp = new byte[32];
      TokenAnchor tokenAnchorWinner = null;

      TokensAnchorDetectedInBlock.ForEach(t =>
      {
        byte[] differenceHash = targetValue.SubtractByteWise(
          t.HashBlockReferenced);

        if (differenceHash.IsGreaterThan(biggestDifferenceTemp))
        {
          biggestDifferenceTemp = differenceHash;
          tokenAnchorWinner = t;
        }
      });

      ($"The winning anchor token is {tokenAnchorWinner} referencing block " +
        $"{tokenAnchorWinner.HashBlockReferenced.ToHexString()}.").Log(this, LogFile, LogEntryNotifier);

      return tokenAnchorWinner.HashBlockReferenced;
    }
  }
}
