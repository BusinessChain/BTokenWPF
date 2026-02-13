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


        public MessageNetworkProtocol(string command)
          : this(command, new byte[0])
        { }

        public MessageNetworkProtocol(string command, byte[] payload)
          : this(command, payload, payload.Length)
        { }

        public MessageNetworkProtocol(string command, byte[] payload, int lengthPayload)
        {
          Command = command;
          Payload = payload;

          LengthDataPayload = lengthPayload;
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
