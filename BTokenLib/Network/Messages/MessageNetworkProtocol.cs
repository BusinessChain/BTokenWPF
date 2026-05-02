using System;
using System.Linq;
using System.Text;


namespace BTokenLib
{
  partial class Network
  {
    abstract class MessageNetworkProtocol
    {
      public Network Network;

      public string Command;
      public byte[] Payload;
      public int LengthDataPayload;

      public DOSMonitorPer10Minutes DOSMonitor;


      public MessageNetworkProtocol(string command)
        : this(command, new byte[0], null)
      { }

      public MessageNetworkProtocol(string command, Network network)
        : this(command, new byte[0], network)
      { }

      public MessageNetworkProtocol(string command, byte[] payload, Network network)
      {
        Network = network;

        Command = command;
        Payload = payload;

        LengthDataPayload = payload.Length;
      }

      public virtual byte[] GetPayloadBuffer()
      {
        return Payload;
      }

      public abstract void Run(Peer peer);

    }
  }
}
