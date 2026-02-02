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
        public const int CommandSize = 12;
        public const int LengthSize = 4;
        public const int ChecksumSize = 4;

        const int HeaderSize = CommandSize + LengthSize + ChecksumSize;
        byte[] MessageHeader = new byte[HeaderSize];
        public static readonly byte[] MagicBytes = new byte[4] { 0xF9, 0xBE, 0xB4, 0xD9 };

        public string Command;

        public byte[] Payload;
        public int OffsetPayload;
        public int LengthDataPayload;


        public MessageNetworkProtocol(string command)
          : this(command, new byte[0])
        { }

        public MessageNetworkProtocol(string command, byte[] payload)
          : this(
              command,
              payload,
              0,
              payload.Length)
        { }

        public MessageNetworkProtocol(
          string command,
          byte[] payload,
          int indexPayloadOffset,
          int lengthPayload)
        {
          Command = command;
          Payload = payload;

          OffsetPayload = indexPayloadOffset;
          LengthDataPayload = lengthPayload;
        }

        public abstract Task Run(Peer peer);

        public abstract void ParsePayload();
      }
    }
  }
}
