using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

using LiteDB;


namespace BTokenLib
{
  partial class Network
  {
    const int TIME_LOOP_SYNCHRONIZER_SECONDS = 60;

    readonly object LOCK_Synchronizations = new object();
    Synchronization SynchronizationLocal;
    List<Synchronization> SynchronizationsInProgress = new();

    bool TryInsertSynchronization(ref Synchronization sync)
    {
      // bei diesem Lock sollten keine Blöcke mehr in die Syncs inserted werden.
      // LOCK_BlockInsertion muss evt mit LOCK_Synchronizations zusammengelegt werden.
      lock (LOCK_Synchronizations) 
      {
        if (sync.IsHeaderTipStrongerThanBlockTip(SynchronizationLocal))
        {
          foreach (Synchronization syncInProgress in SynchronizationsInProgress)
            if (syncInProgress.TryMergeSynchronization(sync))
            {
              sync = syncInProgress;
              return true;
            }

          SynchronizationsInProgress.Add(sync);
          return true;
        }

        return false;
      }
    }

    void UpdateSynchronization(Synchronization synchronization)
    {
      lock (LOCK_Synchronizations)
      {
        if (SynchronizationLocal.TryReorgToken(synchronization))
          SynchronizationLocal = synchronization;

        foreach (Synchronization syncInProgress in SynchronizationsInProgress)
          if (!syncInProgress.IsHeaderTipStrongerThanBlockTip(SynchronizationLocal))
            syncInProgress.FlagIsAborted = true;
      }
    }

    bool TryConnectHeaderToChain(Header header)
    {
      lock (Lock_StateNetwork)
      {
        Header headerInChain = HeaderTip;

        do
        {
          if (header.HashPrevious.IsAllBytesEqual(headerInChain.Hash))
          {
            header.AppendToHeader(headerInChain);
            return true;
          }

          headerInChain = headerInChain.HeaderPrevious;
        } while (headerInChain != null);

        return false;
      }
    }
  }
}