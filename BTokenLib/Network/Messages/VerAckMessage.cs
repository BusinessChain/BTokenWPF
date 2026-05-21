using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BTokenLib
{
  partial class Network
  {
    class VerAckMessage : MessageNetworkProtocol
    {
      public const string Command = "verack";

      public List<byte[]> Locator;

      public VerAckMessage()
      { }

      public static async Task Send(Peer peer)
      {
        await peer.SendMessage(Command, 0, new byte[0]);
      }

      public override void Run(Peer peer)
      {
        if (peer.Connection == ConnectionType.OUTBOUND)
          GetHeadersMessage.SendGetHeaders(peer, Locator);
      }

      public override string GetCommand()
      {
        return Command;
      }
    }
  }
}