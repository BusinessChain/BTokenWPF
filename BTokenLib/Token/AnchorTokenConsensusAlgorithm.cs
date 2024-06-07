using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.IO;

namespace BTokenLib
{

  /// <summary>
  /// This class determines the winner anchor token.
  /// It is the anchor token whose tX hash has the greatest difference from the hash of its containing parent block. 
  /// </summary>
  public class AnchorTokenConsensusAlgorithm
  {
    SHA256 SHA256 = SHA256.Create();

    List<TokenAnchor> TokensAnchorsMined = new();
    List<List<TokenAnchor>> TokensAnchorsConfirmed = new();

    string PathBackupAnchorTokenConsensusAlgorithm;


    public AnchorTokenConsensusAlgorithm(string nameToken)
    {
      PathBackupAnchorTokenConsensusAlgorithm = Path.Combine(
        nameToken,
        "BlocksMinedUnconfirmed");

      Directory.CreateDirectory(PathBackupAnchorTokenConsensusAlgorithm);

      LoadImage();
    }

    public void IncludeAnchorTokenConfirmed(
      TokenAnchor tokenAnchor,
      out bool flagTokenAnchorWasSelfMined)
    {
      bool hasSuccessor = false;
      flagTokenAnchorWasSelfMined = false;

      foreach (List<TokenAnchor> branchTokenAnchorsConfirmed in TokensAnchorsConfirmed)
        if (tokenAnchor.TX.IsSuccessorTo(branchTokenAnchorsConfirmed.Last().TX))
        {
          branchTokenAnchorsConfirmed.Add(tokenAnchor);
          hasSuccessor = true;
        }

      if (!hasSuccessor)
        TokensAnchorsConfirmed.Add(new() { tokenAnchor });

      if (tokenAnchor.TX.Hash.HasEqualElements(TokensAnchorsMined[0].TX.Hash))
      {
        TokensAnchorsMined.RemoveAt(0);
        flagTokenAnchorWasSelfMined = true;
      }
    }

    public void IncludeAnchorTokenMined(TokenAnchor tokenAnchor)
    {
      if (TokensAnchorsMined.Count > 0 && tokenAnchor.TX.IsSuccessorTo(TokensAnchorsMined.Last().TX))
        TokensAnchorsMined.Add(tokenAnchor);
      else
        TokensAnchorsMined = new() { tokenAnchor };
    }

    public bool TryGetAnchorTokenWinner(byte[] hashHeaderAnchor, out TokenAnchor tokenAnchorWinner)
    {
      byte[] targetValue = SHA256.ComputeHash(hashHeaderAnchor);
      byte[] biggestDifferenceTemp = new byte[32];
      tokenAnchorWinner = null;

      foreach (List<TokenAnchor> branchTokenAnchor in TokensAnchorsConfirmed)
        foreach (TokenAnchor tokenAnchor in branchTokenAnchor)
        {
          byte[] differenceHash = targetValue.SubtractByteWise(
            tokenAnchor.TX.Hash);

          if (differenceHash.IsGreaterThan(biggestDifferenceTemp))
          {
            biggestDifferenceTemp = differenceHash;
            tokenAnchorWinner = branchTokenAnchor.Last();
          }
        };

      TokensAnchorsConfirmed.Clear();

      return tokenAnchorWinner != null;
    }
  
    public bool TryGetAnchorTokenRBF(out TokenAnchor anchorTokenOld)
    {
      if (TokensAnchorsMined.Count > 0)
      {
        anchorTokenOld = TokensAnchorsMined.Last();
        TokensAnchorsMined.Remove(TokensAnchorsMined.Last());
        return true;
      }

      anchorTokenOld = null;
      return false;
    }

    public void LoadImage()
    {
      byte[] buffer = File.ReadAllBytes(PathBackupAnchorTokenConsensusAlgorithm);

      // Parse module
    }
  }
}
