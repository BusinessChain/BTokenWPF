using System;
using System.Windows.Automation;


namespace BTokenLib
{
  partial class Network
  {
    partial class Peer
    {
      class TXMessage : MessageNetworkProtocol
      {
        public TXMessage()
          : base("tx")
        {
          // amount bytes per 10 minutes
          DOSMonitor = new DOSMonitorPer10Minutes(maxLevel: 5000000);
          
        }

        public TXMessage(byte[] tXRaw)
          : base("tx")
        {
          Payload = tXRaw;
          LengthDataPayload = Payload.Length;
        }

        public override MessageNetworkProtocol Create()
        {
          return new TXMessage();
        }

        public override void RunMessage(Peer peer)
        {

        }
      }
    }
  }
}
