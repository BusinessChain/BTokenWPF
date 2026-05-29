using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace BTokenLib
{
  partial class Network
  {
    class VerAckMessage : MessageNetworkProtocol
    {
      public const string Command = "verack";

      public VerAckMessage()
      { }

      public static async Task Send(Peer peer)
      {
        await peer.SendMessage(Command, 0, new byte[0]);
      }

      public override async Task Run(Peer peer)
      {
        if (peer.Connection == ConnectionType.OUTBOUND)
          peer.StartHeaderSync();
      }

      public override string GetCommand()
      {
        return Command;
      }
    }
  }
}