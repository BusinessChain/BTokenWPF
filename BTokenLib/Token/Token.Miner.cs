using System;
using System.Linq;
using System.Threading;


namespace BTokenLib
{
  public abstract partial class Token
  {
    public bool IsMining;

    public void StopMining()
    {
      IsMining = false;
    }

    public void StartMining()
    {
      if (IsMining)
        return;

      IsMining = true;

      $"Start {GetName()} miner".Log(this, LogEntryNotifier);

      new Thread(RunMining).Start();
    }

    protected abstract void RunMining();
  }
}
