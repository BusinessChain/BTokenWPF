using System;
using System.Net;
using System.Threading.Tasks;


namespace BTokenLib
{
  partial class Network
  {
    partial class Peer
    {
      abstract class StateProtocolPeer
      {
        public StateProtocol IDState;

        public abstract Task<StateProtocol> Run(Peer peer);
      }
    }
  }
}
