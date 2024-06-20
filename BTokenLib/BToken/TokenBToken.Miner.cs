using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Threading;

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

    string PathTokensAnchorMined;

    List<Block> BocksMined = new();
    string PathBlocksMined;

    List<TokenAnchor> TokensAnchorMined = new();


    protected override async void RunMining()
    {
      $"Miner {this} starts.".Log(this, LogFile, LogEntryNotifier);

      LoadTokensAnchorMined();

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

            TokenAnchor tokenAnchor = MineBlock(numberSequence: 0);

            if (TokenParent.TryBroadcastAnchorToken(tokenAnchor))
            {
              IncludeAnchorTokenMined(tokenAnchor);

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

    public TokenAnchor MineBlock(int numberSequence)
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

      TokenAnchor tokenAnchor = new();

      tokenAnchor.HashBlockReferenced = block.Header.Hash;
      tokenAnchor.HeightBlockReferenced = block.Header.Height;
      tokenAnchor.HashBlockPreviousReferenced = block.Header.HashPrevious;
      tokenAnchor.IDToken = IDToken;
      tokenAnchor.FeeSatoshiPerByte = FeeSatoshiPerByteAnchorToken;
      tokenAnchor.NumberSequence = numberSequence;

      return tokenAnchor;
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
      if (
        headerAnchor.HashChild != null &&
        TryGetBlockMined(headerAnchor.HashChild, out Block blockMined))
      {
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

      TokenAnchor tokenAnchorOld = TokensAnchorMined.FindLast(t => t != null);

      if (tokenAnchorOld != null)
      {
        $"RBF anchor token {tokenAnchorOld}.".Log(this, LogFile, LogEntryNotifier);

        FeeSatoshiPerByteAnchorToken *= FACTOR_INCREMENT_FEE_PER_BYTE_ANCHOR_TOKEN;

        TokenAnchor tokenAnchorNew = MineBlock(tokenAnchorOld.NumberSequence + 1);

        if(TokenParent.TryRBFAnchorToken(tokenAnchorOld, tokenAnchorNew))
          IncludeAnchorTokenMined(tokenAnchorNew);
      }

      TokensAnchorMined.Clear();
      File.Delete(PathTokensAnchorMined);
    }

    public void IncludeAnchorTokenMined(TokenAnchor tokenAnchor)
    {
      if (TokensAnchorMined.Count > 0 && tokenAnchor.TX.IsSuccessorTo(TokensAnchorMined.Last().TX))
        TokensAnchorMined.Add(tokenAnchor);
      else
        TokensAnchorMined = new() { tokenAnchor };

      WriteTokensAnchorMinedToDisk();
    }

    public void LoadTokensAnchorMined()
    {
      while (true)
        try
        {
          TokensAnchorMined.Clear();

          using (FileStream fileStream = new(
            PathTokensAnchorMined,
            FileMode.Open,
            FileAccess.Read,
            FileShare.None))
          {
            while (fileStream.Position < fileStream.Length)
            {
              TX tX = TokenParent.ParseTX(fileStream, SHA256Miner);

              if (tX.TryGetAnchorToken(out TokenAnchor tokenAnchor))
                TokensAnchorMined.Add(tokenAnchor);
              else
                throw new InvalidOperationException($"Error: Could not load anchor token mined from tX {tX}.");
            }
          }

          return;
        }
        catch (FileNotFoundException)
        {
          return;
        }
        catch (Exception ex)
        {
          ($"{ex.GetType().Name} when attempting to load mined anchor token {PathTokensAnchorMined}: {ex.Message}.\n" +
            $"Retry in {TIMEOUT_FILE_RELOAD_SECONDS} seconds.").Log(this, LogEntryNotifier);

          Thread.Sleep(TIMEOUT_FILE_RELOAD_SECONDS * 1000);
        }
    }

    void WriteTokensAnchorMinedToDisk()
    {
      while (true)
        try
        {
          using (FileStream fileStreamBlock = new(
            PathTokensAnchorMined,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None))
          {
            TokensAnchorMined.ForEach(t => t.TX.WriteToStream(fileStreamBlock));
          }

          break;
        }
        catch (Exception ex)
        {
          ($"{ex.GetType().Name} when writing TokensAnchorMined to file:\n" +
            $"{ex.Message}\n " +
            $"Try again in {TIMEOUT_FILE_RELOAD_SECONDS} seconds ...").Log(this, LogEntryNotifier);

          Thread.Sleep(TIMEOUT_FILE_RELOAD_SECONDS);
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

      BocksMined.Clear();

      var files = new DirectoryInfo(PathBlocksMined).GetFiles();

      foreach (FileInfo file in files)
        file.Delete();

      return blockMined != null;
    }

    public override void SignalAnchorTokenDetected(TokenAnchor tokenAnchor)
    {
      TokensAnchorMined.RemoveAll(t => t.TX.Hash.IsAllBytesEqual(tokenAnchor.TX.Hash));
    }
  }
}
