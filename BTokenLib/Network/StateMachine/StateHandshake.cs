using System;
using System.Net;
using System.Threading.Tasks;


namespace BTokenLib
{
  partial class Network
  {
    partial class Peer
    {
      class StateHandshake : StateProtocolPeer
      {
        public StateHandshake()
        {
          IDState = StateProtocol.Handshake;
        }

        public override async Task<StateProtocol> Run(Peer peer)
        {
          peer.SetTimer("Timeout handshake.", TIMEOUT_HANDSHAKE_MILLISECONDS);

          if (peer.Connection == ConnectionType.OUTBOUND)
            peer.SendVersion();

          bool flagReceivedVersion = false;
          bool flagReceivedVerack = false;

          while(!flagReceivedVersion || !flagReceivedVerack)
          {
            MessageNetworkProtocol message = await peer.ReceiveNextMessage();

            if (message.Command == "verack")
            {
              flagReceivedVerack = true;
            }
            else if (message.Command == "version")
            {
              flagReceivedVersion = true;
              peer.SendMessage(new VerAckMessage());

              if (peer.Connection == ConnectionType.INBOUND)
                peer.SendVersion();
            }
          }
            
          return StateProtocol.Idle;
        }
      }
    }
  }
}
