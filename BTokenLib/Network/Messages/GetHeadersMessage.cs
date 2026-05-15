using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;


namespace BTokenLib
{
  partial class Network
  {
    partial class Peer
    {
      class GetHeadersMessage : MessageNetworkProtocol
      {
        const string Command = "getheaders";

        Header HaederAncestorSentLast;


        public GetHeadersMessage(Network network)
        { }

        public GetHeadersMessage(List<byte[]> headerLocator, uint versionProtocol)
        {
          List<byte> payload = new();

          payload.AddRange(BitConverter.GetBytes(versionProtocol));
          payload.AddRange(VarInt.GetBytes(headerLocator.Count()));

          for (int i = 0; i < headerLocator.Count(); i++)
            payload.AddRange(headerLocator.ElementAt(i));

          payload.AddRange("0000000000000000000000000000000000000000000000000000000000000000".ToBinary());

          Payload = payload.ToArray();
          LengthDataPayload = Payload.Length;
        }

        public override void Run(Peer peer)
        {
          DOSMonitor.Increment(1);

          int startIndex = 0;

          byte[] version = new byte[4];
          Array.Copy(Payload, startIndex, version, 0, version.Length);
          startIndex += version.Length;

          int countHeaderLocator = VarInt.GetInt(Payload, ref startIndex);

          if (countHeaderLocator > 101)
            throw new ProtocolException($"Too many ({countHeaderLocator}) headers in locator.");

          List<byte[]> hashesLocator = new();

          for (int i = 0; i < countHeaderLocator; i += 1)
          {
            byte[] hashLocator = new byte[32];
            Array.Copy(Payload, startIndex, hashLocator, 0, hashLocator.Length);
            startIndex += hashLocator.Length;

            hashesLocator.Add(hashLocator);
          }

          if (Network.TryLoadHeaderAncestor(hashesLocator, out Header headerAncestor))
          {
            HeadersMessage.SendHeaders(peer, headerAncestor.HeaderNext);

            if(headerAncestor.Height > HaederAncestorSentLast.Height)
              DOSMonitor.Decrement(1);

            HaederAncestorSentLast = headerAncestor;
          }
        }

        public override string GetCommand()
        {
          return Command;
        }
      }
    }
  }
}