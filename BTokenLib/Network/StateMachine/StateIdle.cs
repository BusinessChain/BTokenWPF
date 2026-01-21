using System;
using System.Net;
using System.Threading.Tasks;


namespace BTokenLib
{
  partial class Network
  {
    partial class Peer
    {
      class StateIdle : StateProtocolPeer
      {
        public StateIdle()
        {
          IDState = StateProtocol.Idle;
        }

        public override async Task<StateProtocol> Run(Peer peer)
        {
          peer.SetTimer();

          MessageNetworkProtocol message = await peer.ReceiveNextMessage();

          if (message.Command == "headers")
          {
            return StateProtocol.HeaderDownload;
          }
          else if (message.Command == "ping")
          {
            return StateProtocol.ping;
          }
          else
            return StateProtocol.Idle;
        }
      }
    }
  }
}
