using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Security.Cryptography;


namespace BTokenLib
{
  partial class Network
  {
    partial class Peer
    {
      class HeadersMessage : MessageNetworkProtocol
      {
        const string Command = "headers";

        const int MaxCountHeaders = 2000;

        Block BlockDownload;

        SHA256 SHA256 = SHA256.Create();


        public HeadersMessage(Block blockDownload)
        {
          DOSMonitor = new DOSMonitorPer10Minutes(maxLevel: 5);
          BlockDownload = blockDownload;
        }


        public override void Run(Peer peer)
        {
          DOSMonitor.Increment(1);

          int startIndex = 0;
          int countHeaders = VarInt.GetInt(Payload, ref startIndex);

          if (countHeaders > MaxCountHeaders)
            throw new ProtocolException($"Too many headers {countHeaders} in headers message.");
          else if (countHeaders > 0)
          {
            Header headerRoot = ParseHeaderchain(countHeaders, ref startIndex);

            if (Network.SynchronizationRoot.TryExtendHeaderchain(
              headerRoot,
              out List<byte[]> headerslocator,
              BlockDownload))
            {
              DOSMonitor.Decrement(1);
            }

            if (headerslocator != null)
              peer.SendGetHeaders(headerslocator);
          }
          else if (countHeaders == 0 && BlockDownload.Header != null)
            GetDataMessage.SendBlockRequest(peer, BlockDownload.Header.Hash);
        }

        Header ParseHeaderchain(int countHeaders, ref int startIndex)
        {
          Header headerRoot = Network.Token.ParseHeader(Payload, ref startIndex, SHA256);
          VarInt.GetInt(Payload, ref startIndex);

          Header headerTip = headerRoot;

          countHeaders -= 1;

          while (countHeaders > 0)
          {
            Header header = Network.Token.ParseHeader(Payload, ref startIndex, SHA256);
            VarInt.GetInt(Payload, ref startIndex);

            header.AppendToHeader(headerTip);
            headerTip.HeaderNext = header;
            headerTip = header;

            countHeaders -= 1;
          }

          return headerRoot;
        }

        public static async Task SendHeaders(Peer peer, Header headerRoot)
        {
          List<byte> bufferList = new();

          int i = 0;

          while(headerRoot != null && i < MaxCountHeaders)
          {
            bufferList.AddRange(headerRoot.Serialize());
            headerRoot = headerRoot.HeaderNext;
            i += 1;
          }

          bufferList.InsertRange(0, VarInt.GetBytes(bufferList.Count));

          byte[] buffer = bufferList.ToArray();

          await peer.SendMessage(Command, buffer.Length, buffer);
        }

        public override string GetCommand()
        {
          return Command;
        }
      }
    }
  }
}