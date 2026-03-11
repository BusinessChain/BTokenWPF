using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

using LiteDB;
using System.Threading;


namespace BTokenLib
{
  partial class Network
  {
    const int TIME_LOOP_SYNCHRONIZER_SECONDS = 60;

    readonly object LOCK_FlagSynchronizationsLocked = new object();
    bool FlagSynchronizationsLocked;
    Synchronization SynchronizationLocal;
    List<Synchronization> SynchronizationsInProgress = new();

    bool TryInsertSynchronization(ref Synchronization sync)
    {
      if (!TryLockSynchronizations())
        return false;

      try
      {
        if (sync.IsHeaderTipStrongerThanBlockTip(SynchronizationLocal))
        {
          foreach (Synchronization syncInProgress in SynchronizationsInProgress)
            if (syncInProgress.TryMerge(sync))
            {
              sync = syncInProgress;
              goto Skip_Add2SynchronizationsInProgress;
            }

          SynchronizationsInProgress.Add(sync);

        Skip_Add2SynchronizationsInProgress:

          return true;
        }
      }
      finally
      {
        ReleaseLockSynchronizations();
      }

      return false;
    }

    bool TryLockSynchronizations()
    {
      int randomTimeout = Random.Shared.Next(5,10);

      while (randomTimeout > 0)
      {
        lock (LOCK_FlagSynchronizationsLocked)
          if (!FlagSynchronizationsLocked)
          {
            FlagSynchronizationsLocked = true;
            return true;
          }

        Thread.Sleep(10);
        randomTimeout -= 1;
      }

      return false;
    }

    void ReleaseLockSynchronizations()
    {
      lock (LOCK_FlagSynchronizationsLocked)
        FlagSynchronizationsLocked = false;
    }

    void UpdateSynchronization(Synchronization sync)
    {
      if(TryLockSynchronizations())
      {
        if (SynchronizationLocal.TryReorgToken(sync))
          SynchronizationLocal = sync;

        foreach (Synchronization syncInProgress in SynchronizationsInProgress)
          if (!syncInProgress.IsHeaderTipStrongerThanBlockTip(SynchronizationLocal))
            syncInProgress.FlagIsAborted = true;

        ReleaseLockSynchronizations();
      }
    }

    bool TryConnectHeaderToChain(ref Header header)
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