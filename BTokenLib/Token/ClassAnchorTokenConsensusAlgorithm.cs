using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.IO;
using System.Threading;

namespace BTokenLib
{
  partial class TokenBToken
  {
    /// <summary>
    /// This class determines the winner anchor token.
    /// It is the anchor token whose tX hash has the greatest difference from the hash of its containing parent block. 
    /// </summary>
    class ClassAnchorTokenConsensusAlgorithm
    {
      Token Token;

      SHA256 SHA256 = SHA256.Create();

      List<TokenAnchor> TokensAnchorMined = new();
      List<List<TokenAnchor>> TokensAnchorsConfirmed = new();

      string PathTokensAnchorMined;



      public ClassAnchorTokenConsensusAlgorithm(Token token)
      {
        Token = token;

        PathTokensAnchorMined = Path.Combine(token.GetName(), "TokensAnchorMined");

        LoadTokensAnchorMined();
      }

      public void IncludeAnchorTokenConfirmed(TokenAnchor tokenAnchor)
      {
        bool hasSuccessor = false;

        foreach (List<TokenAnchor> branchTokenAnchorsConfirmed in TokensAnchorsConfirmed)
          if (tokenAnchor.TX.IsSuccessorTo(branchTokenAnchorsConfirmed.Last().TX))
          {
            branchTokenAnchorsConfirmed.Add(tokenAnchor);
            hasSuccessor = true;
          }

        if (!hasSuccessor)
          TokensAnchorsConfirmed.Add(new() { tokenAnchor });
      }

      public void IncludeAnchorTokenMined(TokenAnchor tokenAnchor)
      {
        if (TokensAnchorMined.Count > 0 && tokenAnchor.TX.IsSuccessorTo(TokensAnchorMined.Last().TX))
          TokensAnchorMined.Add(tokenAnchor);
        else
          TokensAnchorMined = new() { tokenAnchor };

        WriteTokensAnchorMinedToDisk();
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
              $"Try again in {TIMEOUT_FILE_RELOAD_SECONDS} seconds ...").Log(this, Token.LogEntryNotifier);

            Thread.Sleep(TIMEOUT_FILE_RELOAD_SECONDS);
          }
      }

      public bool TryGetAnchorTokenWinner(
        Header headerAnchor,
        out TokenAnchor tokenAnchorWinner)
      {
        tokenAnchorWinner = null;

        if (TokensAnchorsConfirmed.Count == 0)
          return false;

        byte[] targetValue = SHA256.ComputeHash(headerAnchor.Hash);
        byte[] biggestDifferenceTemp = new byte[32];

        foreach (List<TokenAnchor> branchTokenAnchor in TokensAnchorsConfirmed)
          foreach (TokenAnchor tokenAnchor in branchTokenAnchor)
          {
            byte[] differenceHash = targetValue.SubtractByteWise(
              tokenAnchor.TX.Hash);

            if (differenceHash.IsGreaterThan(biggestDifferenceTemp))
            {
              biggestDifferenceTemp = differenceHash;
              tokenAnchorWinner = branchTokenAnchor.Last();
              headerAnchor.HashChild = tokenAnchorWinner.HashBlockReferenced;
            }
          };

        TokensAnchorsConfirmed.Clear();

        return true;
      }

      public bool TryGetAnchorTokenRBF(out TokenAnchor anchorTokenOld)
      {
        anchorTokenOld = TokensAnchorMined.FindLast(t => t != null);
        TokensAnchorMined.Clear();
        return anchorTokenOld != null;
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
              while(fileStream.Position < fileStream.Length)
              {
                TX tX = Token.TokenParent.ParseTX(fileStream, SHA256);

                TokenAnchor tokenAnchor = tX.GetAnchorToken();
                
                if (tokenAnchor != null)
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
              $"Retry in {TIMEOUT_FILE_RELOAD_SECONDS} seconds.").Log(this, Token.LogEntryNotifier);

            Thread.Sleep(TIMEOUT_FILE_RELOAD_SECONDS * 1000);
          }
      }
    }
  }
}
