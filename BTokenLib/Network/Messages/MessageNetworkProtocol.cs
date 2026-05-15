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

      public byte[] Payload;
      public int LengthDataPayload;

      public DOSMonitorPer10Minutes DOSMonitor;


      public MessageNetworkProtocol()
        : this(new byte[0], null)
      { }

      public MessageNetworkProtocol(Network network)
        : this(new byte[0], network)
      { }

      public MessageNetworkProtocol(byte[] payload, Network network)
      {
        Network = network;

        Payload = payload;

        LengthDataPayload = payload.Length;
      }

      public virtual byte[] GetPayloadBuffer()
      {
        return Payload;
      }

      public abstract void Run(Peer peer);

      public abstract string GetCommand();
    }
  }
}
