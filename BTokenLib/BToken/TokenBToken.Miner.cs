using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace BTokenLib
{
  public partial class TokenBToken : Token
  {
    const int COUNT_BYTES_PER_BLOCK_MAX = 4000000;
    const int TIMESPAN_MINING_ANCHOR_TOKENS_SECONDS = 5;
    const int TIME_MINER_PAUSE_AFTER_RECEIVE_PARENT_BLOCK_SECONDS = 10;
    const double FACTOR_INCREMENT_FEE_PER_BYTE_ANCHOR_TOKEN = 1.02;
    const double MINIMUM_FEE_SATOSHI_PER_BYTE_ANCHOR_TOKEN = 0.1;

    SHA256 SHA256Miner = SHA256.Create();
    Random RandomGeneratorMiner = new();

    double FeeSatoshiPerByteAnchorToken;

    List<Block> BocksMined = new();
    string PathBlocksMined;

    List<TokenAnchor> TokensAnchorMinedUnconfirmed = new();


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

            BlockBToken block = MineBlock(out TokenAnchor tokenAnchor);

            if (TokenParent.TryBroadcastAnchorToken(tokenAnchor))
            {
              $"Mine block {block}. {BocksMined.Count} blocks in BocksMined.".Log(this, LogFile, LogEntryNotifier);

              // timeMSLoop = (int)(tokenAnchor.TX.Fee * TIMESPAN_DAY_SECONDS * 1000 /
              // COUNT_SATOSHIS_PER_DAY_MINING);

              // timeMSLoop = RandomGeneratorMiner.Next(
              // timeMSCreateNextAnchorToken / 2,
              // timeMSCreateNextAnchorToken * 3 / 2);
            }
          }

          ReleaseLock();
        }

        await Task.Delay(timeMinerLoopMilliseconds).ConfigureAwait(false);
      }

      $"Exit BToken miner.".Log(this, LogFile, LogEntryNotifier);
    }

    BlockBToken MineBlock(out TokenAnchor tokenAnchor)
    {
      BlockBToken block = new(this);

      block.TXs.AddRange(TXPool.GetTXs(COUNT_BYTES_PER_BLOCK_MAX, out long feeTXs));

      int height = HeaderTip.Height + 1;

      long blockReward = BLOCK_REWARD_INITIAL >> height / PERIOD_HALVENING_BLOCK_REWARD;
      blockReward += feeTXs;

      TXBToken tXCoinbase = ((WalletBToken)Wallet).CreateTXCoinbase(blockReward, height);

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

      block.Header.ComputeHash(SHA256Miner);

      BocksMined.Add(block);
      WriteBlockMinedToDisk(block);

      tokenAnchor = new();

      tokenAnchor.HashBlockReferenced = block.Header.Hash;
      tokenAnchor.HeightBlockReferenced = block.Header.Height;
      tokenAnchor.HashBlockPreviousReferenced = block.Header.HashPrevious;
      tokenAnchor.IDToken = IDToken;
      tokenAnchor.FeeSatoshiPerByte = FeeSatoshiPerByteAnchorToken;

      return block;
    }

    void WriteBlockMinedToDisk(Block block)
    {
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
            block.Serialize(fileStreamBlock);
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

    public override void SignalParentBlockInsertion(Header headerAnchor)
    {
      string textLog = $"Signal parent block insertion {headerAnchor}";

      if (headerAnchor.HashesChild.TryGetValue(IDToken, out byte[] hashChildTmp))
        textLog += $" referencing hash child {hashChildTmp.ToHexString()}.";
      else
        textLog += $" referencing no hash child.";

      textLog.Log(this, LogFile, LogEntryNotifier);

      if (
        headerAnchor.HashesChild.TryGetValue(IDToken, out byte[] hashChild) &&
        TryGetBlockMined(hashChild, out Block blockMined))
      {
        $"Clear list Blocks mined, delete {BocksMined.Count} blocks.".Log(this, LogFile, LogEntryNotifier);
        BocksMined.Clear();

        var files = new DirectoryInfo(PathBlocksMined).GetFiles();

        foreach (FileInfo file in files)
          file.Delete();

        $"Insert self mined block {blockMined}.".Log(this, LogFile, LogEntryNotifier);

        try
        {
          InsertBlock(blockMined);
          Network.AdvertizeBlockToNetwork(blockMined);
        }
        catch (Exception ex)
        {
          ($"{ex.GetType().Name} when inserting self mined block {blockMined}:\n" +
            $"{ex.Message}").Log(this, LogFile, LogEntryNotifier);
        }
      }

      if (TokensAnchorMinedUnconfirmed.Count > 0)
      {
        TokenAnchor tokenAnchorOld = TokensAnchorMinedUnconfirmed.Last();

        $"{TokensAnchorMinedUnconfirmed.Count} anchor tokens mined not in parent block. Last being {tokenAnchorOld}.".Log(this, LogFile, LogEntryNotifier);

        FeeSatoshiPerByteAnchorToken *= FACTOR_INCREMENT_FEE_PER_BYTE_ANCHOR_TOKEN;

        BlockBToken block = MineBlock(out TokenAnchor tokenAnchorNew);

        tokenAnchorNew.NumberSequence = tokenAnchorOld.NumberSequence + 1;

        if (TokenParent.TryRBFAnchorToken(tokenAnchorOld, tokenAnchorNew))
        {
          BocksMined.Add(block);
          WriteBlockMinedToDisk(block);

          ($"RBF old anchor token {tokenAnchorOld} referencing {tokenAnchorOld.HashBlockReferenced.ToHexString()}\n" +
            $" with {tokenAnchorNew} referenching {tokenAnchorNew.HashBlockReferenced.ToHexString()}.").Log(this, LogFile, LogEntryNotifier);
        }
      }
    }

    bool TryGetBlockMined(byte[] hashBlock, out Block blockMined)
    {
      blockMined = BocksMined.Find(b => b.Header.Hash.IsAllBytesEqual(hashBlock));

      if (blockMined == null)
      {
        string pathBlockMined = Path.Combine(PathBlocksMined, hashBlock.ToHexString());

        while (true)
          try
          {
            using (FileStream fileStream = new(
              pathBlockMined,
              FileMode.Open,
              FileAccess.Read,
              FileShare.None))
            {
              try
              {
                blockMined = ParseBlock(fileStream);
              }
              catch (Exception ex)
              {
                $"{ex.GetType().Name} when attempting to parse file {pathBlockMined}: {ex.Message}"
                  .Log(this, LogEntryNotifier);

                blockMined = null;

                break;
              }
            }

            break;
          }
          catch (FileNotFoundException)
          {
            break;
          }
          catch (IOException ex)
          {
            ($"{ex.GetType().Name} when attempting to load file {pathBlockMined}: {ex.Message}.\n" +
              $"Retry in {TIMEOUT_FILE_RELOAD_SECONDS} seconds.").Log(this, LogEntryNotifier);

            Thread.Sleep(TIMEOUT_FILE_RELOAD_SECONDS * 1000);
            continue;
          }
      }

      if (blockMined == null)
        $"Did not find self mined block {blockMined}.".Log(this, LogFile, LogEntryNotifier);
      else
        $"Found self mined block {blockMined}.".Log(this, LogFile, LogEntryNotifier);

      return blockMined != null;
    }

    public override void ReceiveAnchorTokenConfirmed(TokenAnchor tokenAnchor)
    {
      TokenAnchor tokenAnchorMined = TokensAnchorMinedUnconfirmed.Find(
        t => tokenAnchor.TX.Hash.IsAllBytesEqual(tokenAnchor.TX.Hash));

      if (tokenAnchorMined != null)
      {
        $"Remove anchor token {tokenAnchorMined} in TokensAnchorMined.".Log(this, LogEntryNotifier);
        TokensAnchorMinedUnconfirmed.RemoveAt(0);
      }
      else
        $"Anchor token {tokenAnchorMined} not found in TokensAnchorMined.".Log(this, LogEntryNotifier);
    }

    public override void CacheAnchorTokenUnconfirmedIfSelfMined(TokenAnchor tokenAnchor)
    {
      if (!BocksMined.Any(b => b.Header.Hash.IsAllBytesEqual(tokenAnchor.HashBlockReferenced)))
        return;

      TokensAnchorMinedUnconfirmed.RemoveAll(t => tokenAnchor.TX.IsReplacementByFee(t.TX));

      TokensAnchorMinedUnconfirmed.Add(tokenAnchor);

      $"Included anchor token {tokenAnchor}. {TokensAnchorMinedUnconfirmed.Count} anchor tokens in TokensAnchorMined."
        .Log(this, LogFile, LogEntryNotifier);
    }
  }
}
