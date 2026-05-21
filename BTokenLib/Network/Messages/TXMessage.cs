using System;
using System.Windows.Automation;


namespace BTokenLib
{
  partial class Network
  {
    class TXMessage : MessageNetworkProtocol
    {
      public const string Command = "tx";

      public TXMessage()
      {
        // amount bytes per 10 minutes
        DOSMonitor = new DOSMonitorPer10Minutes(maxLevel: 5000000);

      }

      public TXMessage(byte[] tXRaw)
      {
        Payload = tXRaw;
        LengthDataPayload = Payload.Length;
      }

      public override void Run(Peer peer)
      {

      }

      public override string GetCommand()
      {
        return Command;
      }
    }
  }
}
