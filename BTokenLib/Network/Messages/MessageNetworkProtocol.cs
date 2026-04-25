using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace BTokenLib
{
  partial class Network
  {
    partial class Peer
    {
      abstract class MessageNetworkProtocol
      {
        public string Command;
        public byte[] Payload;
        public int LengthDataPayload;

        public DOSMonitorPer10Minutes DOSMonitor;


        public MessageNetworkProtocol(string command)
          : this(command, new byte[0])
        { }

        public MessageNetworkProtocol(string command, byte[] payload)
        {
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
}
