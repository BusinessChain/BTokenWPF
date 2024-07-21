using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Collections.Generic;
using System.Security.Cryptography;


namespace BTokenLib
{
  public class PoolTXBitcoin
  {
    readonly object LOCK_TXsPool = new(); 
    const bool FLAG_ENABLE_RBF = true;

    Dictionary<byte[], List<(TXInputBitcoin, TXBitcoin)>> InputsPool =
      new(new EqualityComparerByteArray());

    Dictionary<byte[], TXBitcoin> TXPoolDict =
      new(new EqualityComparerByteArray());

    List<TX> TXsGet = new();
    int CountMaxTXsGet;

    public FileStream FileTXPoolDict;

    Dictionary<int, bool> FlagTXAddedPerThreadID = new();

    Token Token;


    public PoolTXBitcoin(Token token)
    {
      Token = token;

      FileTXPoolDict = new FileStream(
        Path.Combine(token.GetName(), "TXPoolDict"), 
        FileMode.OpenOrCreate,
        FileAccess.ReadWrite,
        FileShare.Read);

      LoadFromFile();
    }

    void LoadFromFile()
    {
      SHA256 sHA256 = SHA256.Create();

      while (FileTXPoolDict.Position < FileTXPoolDict.Length)
      {
        TXBitcoin tX = (TXBitcoin)Token.ParseTX(FileTXPoolDict, sHA256);
        AddTX(tX);
      }
    }

    public bool TryGetTX(byte[] hashTX, out TXBitcoin tX)
    {
      lock (LOCK_TXsPool)
        return TXPoolDict.TryGetValue(hashTX, out tX);
    }

    public bool GetFlagTXAddedSinceLastInquiry()
    {
      int iDThread = Thread.CurrentThread.ManagedThreadId;

      lock (LOCK_TXsPool)
      {
        if(FlagTXAddedPerThreadID.TryGetValue(iDThread, out bool flagTXAdded) && !flagTXAdded)
          return false;

        FlagTXAddedPerThreadID[iDThread] = false;
        return true;
      }
    }

    void AddTX(TXBitcoin tX)
    {
      TXPoolDict.Add(tX.Hash, tX);

      foreach (TXInputBitcoin tXInput in tX.Inputs)
        if (InputsPool.TryGetValue(tXInput.TXIDOutput, out List<(TXInputBitcoin input, TXBitcoin)> inputsInPool))
          inputsInPool.Add((tXInput, tX));
        else
          InputsPool.Add(tXInput.TXIDOutput, new List<(TXInputBitcoin, TXBitcoin)>() { (tXInput, tX) });
    }

    public bool TryAddTX(TXBitcoin tX)
    {
      bool flagRemoveTXInPoolBeingRBFed = false;
      TX tXInPoolBeingRBFed = null;

      try
      {
        lock (LOCK_TXsPool)
        {
          foreach (TXInputBitcoin tXInput in tX.Inputs)
            if (InputsPool.TryGetValue(tXInput.TXIDOutput, out List<(TXInputBitcoin, TXBitcoin)> inputsInPool))
              foreach ((TXInputBitcoin input, TX tX) tupleInputsInPool in inputsInPool)
                if (tupleInputsInPool.input.OutputIndex == tXInput.OutputIndex)
                {
                  ($"Output {tXInput.TXIDOutput.ToHexString()} - {tXInput.OutputIndex} referenced by tX {tX} " +
                    $"already referenced by tX {tupleInputsInPool.tX}.").Log(this, Token.LogEntryNotifier);

                  if (
                    FLAG_ENABLE_RBF &&
                    tXInput.Sequence > tupleInputsInPool.input.Sequence)
                  {
                    ($"Replace tX {tupleInputsInPool.tX} (sequence = {tupleInputsInPool.input.Sequence}) " +
                      $"with tX {tX} (sequence = {tXInput.Sequence}).").Log(this, Token.LogEntryNotifier);

                    flagRemoveTXInPoolBeingRBFed = true;
                    tXInPoolBeingRBFed = tupleInputsInPool.tX;
                  }
                  else
                    return false;
                }

          if (flagRemoveTXInPoolBeingRBFed)
            RemoveTX(tXInPoolBeingRBFed.Hash, flagRemoveRecursive: true);

          AddTX(tX);
          FileTXPoolDict.Write(tX.TXRaw.ToArray(), 0, tX.TXRaw.Count);

          foreach (int key in FlagTXAddedPerThreadID.Keys.ToList())
            FlagTXAddedPerThreadID[key] = true;

          return true;
        }
      }
      catch (Exception ex)
      {
        $"Exception {ex.GetType().Name} when trying to insert tx {tX} in TXPool."
          .Log(this, Token.LogEntryNotifier);

        return false;
      }
    }

    public List<TX> GetTXs(int countMax = int.MaxValue)
    {
      lock (LOCK_TXsPool)
      {
        FlagTXAddedPerThreadID[Thread.CurrentThread.ManagedThreadId] = false;

        TXsGet.Clear();
        CountMaxTXsGet = countMax;

        foreach (KeyValuePair<byte[], TXBitcoin> tXInPool in TXPoolDict)
          if (TXsGet.Count < CountMaxTXsGet)
            ExtractBranch(tXInPool.Value);
          else
            break;

        return TXsGet.ToList();
      }
    }

    public void RemoveTXs(IEnumerable<byte[]> hashesTX)
    {
      foreach (byte[] hashTX in hashesTX)
        RemoveTX(hashTX, flagRemoveRecursive: false);

      FileTXPoolDict.SetLength(0);

      foreach(TXBitcoin tX in TXPoolDict.Values)
        FileTXPoolDict.Write(tX.TXRaw.ToArray(), 0, tX.TXRaw.Count);
    }

    /// <summary>
    /// Removes a tX and all tXs that reference its outputs.
    /// </summary>
    void RemoveTX(byte[] hashTX, bool flagRemoveRecursive)
    {
      lock (LOCK_TXsPool)
        if (TXPoolDict.Remove(hashTX, out TXBitcoin tX))
        {
          List<(TXInputBitcoin input, TXBitcoin)> tupelInputs = null;

          foreach (TXInputBitcoin tXInput in tX.Inputs)
            if (InputsPool.TryGetValue(tXInput.TXIDOutput, out tupelInputs))
            {
              tupelInputs.RemoveAll(t => t.input.OutputIndex == tXInput.OutputIndex);

              if (tupelInputs.Count == 0)
                InputsPool.Remove(tXInput.TXIDOutput);
            }

          if (flagRemoveRecursive && InputsPool.TryGetValue(hashTX, out tupelInputs))
            foreach ((TXInputBitcoin input, TXBitcoin tX) tupelInputInPool in tupelInputs.ToList())
              RemoveTX(tupelInputInPool.tX.Hash, flagRemoveRecursive: true);
        }
    }

    void ExtractBranch(TXBitcoin tXRoot)
    {
      if (TXsGet.Contains(tXRoot))
        return;

      List<TXBitcoin> tXsBranch = new() { tXRoot };

      TraceTXToLeaf(tXsBranch);

      foreach (TXBitcoin tXBranch in tXsBranch)
      {
        foreach (TXInputBitcoin input in tXBranch.Inputs)
        {
          if (TXsGet.Count >= CountMaxTXsGet)
            return;

          if (TXPoolDict.TryGetValue(input.TXIDOutput, out TXBitcoin tXRootSubBranch))
            ExtractBranch(tXRootSubBranch);
        }

        TXsGet.Add(tXBranch);
      }
    }

    void TraceTXToLeaf(List<TXBitcoin> tXsBranch)
    {
      foreach (TXInputBitcoin input in tXsBranch[0].Inputs)
        if (TXPoolDict.TryGetValue(input.TXIDOutput, out TXBitcoin tXInPool) &&
            !TXsGet.Contains(tXInPool))
        {
          tXsBranch.Insert(0, tXInPool);
          TraceTXToLeaf(tXsBranch);
          return;
        }
    }
  }
}
