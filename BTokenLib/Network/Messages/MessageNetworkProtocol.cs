using System;
using System.Linq;
using System.Text;


namespace BTokenLib
{
  partial class Network
  {
    abstract class MessageNetworkProtocol
    {
      public byte[] Payload;
      public int LengthDataPayload;

      public DOSMonitorPer10Minutes DOSMonitor;


      public MessageNetworkProtocol()
        : this(new byte[0])
      { }

      public MessageNetworkProtocol(byte[] payload)
      {

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
