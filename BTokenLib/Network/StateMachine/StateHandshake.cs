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
            peer.SendMessage(new VersionMessage(
              protocolVersion: ProtocolVersion,
              networkServicesLocal: 0,
              unixTimeSeconds: DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
              networkServicesRemote: 0,
              iPAddressRemote: IPAddress.Loopback,
              portRemote: peer.Network.Port,
              iPAddressLocal: IPAddress.Loopback,
              portLocal: peer.Network.Port,
              nonce: 0,
              userAgent: UserAgent,
              blockchainHeight: 0,
              relayOption: 0x01));

          bool flagReceivedVersion = false;
          bool flagReceivedVerack = false;

          while(!flagReceivedVersion || !flagReceivedVerack)
          {
            MessageNetworkProtocol message = await peer.ReceiveNextMessage();

            if (message is VerAckMessage)
            {
              flagReceivedVerack = true;
            }
            else if (message is VersionMessage)
            {
              flagReceivedVersion = true;
              peer.SendMessage(new VerAckMessage());
            }
          }

          return StateProtocol.Idle;
        }
      }
    }
  }
}
