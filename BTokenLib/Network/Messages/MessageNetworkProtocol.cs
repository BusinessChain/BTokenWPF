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
      class MessageNetworkProtocol
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

        public async Task Receive(Peer peer)
        {
          byte[] magicByte = new byte[1];

          for (int i = 0; i < MagicBytes.Length; i++)
          {
            await peer.ReadBytes(magicByte, 1);

            if (MagicBytes[i] != magicByte[0])
              i = magicByte[0] == MagicBytes[0] ? 0 : -1;
          }

          await peer.ReadBytes(MessageHeader, MessageHeader.Length);

          string command = Encoding.ASCII.GetString(
            MessageHeader.Take(CommandSize).ToArray()).TrimEnd('\0');

          LengthDataPayload = BitConverter.ToInt32(MessageHeader, CommandSize);

          if (LengthDataPayload > Payload.Length)
            throw new ProtocolException($"Received network message payload length " +
              $"exceeds the allowed length of {Payload.Length} bytes for message {Command}.");

          await peer.ReadBytes(Payload, LengthDataPayload);
        }
      }
    }
  }
}
