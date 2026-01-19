using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BTokenLib
{
  partial class Network
  {
    partial class Peer
    {
      class VerAckMessage : MessageNetwork
      {
        public VerAckMessage()
          : base("verack")
        { }


        public override MessageNetwork Create()
        {
          return new VerAckMessage();
        }

        public override void RunMessage(Peer peer)
        {
          peer.Log($"Received verack.");
          peer.SetTimer();

          if (peer.State == StateProtocol.AwaitVerack)
            peer.State = StateProtocol.Idle;
        }
      }
    }
  }
}