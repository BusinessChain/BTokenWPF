using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;


namespace BTokenLib
{
  partial class Network
  {
    class GetHeadersMessage : MessageNetworkProtocol
    {
      public const string Command = "getheaders";

      Header HaederAncestorSentLast;


      public GetHeadersMessage()
      {
      }

      public override async Task Run(Peer peer)
      {
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

        List<Header> headerAncestor = await peer.Network.GetHeaders(hashesLocator);


        //Header headerAncestor = await peer.Network.LoadHeaderAncestor(hashesLocator);

        //if (headerAncestor != null)
        //{
        HeadersMessage.SendHeaders(peer, headerAncestor.HeaderNext);

        //  if (headerAncestor.Height >= HaederAncestorSentLast.Height + HeadersMessage.MaxCountHeaders)
        //    DOSMonitor.Decrement(1);

        //  HaederAncestorSentLast = headerAncestor;
        //}
      }



      public static async Task SendGetHeaders(Peer peer, List<byte[]> locator)
      {
        List<byte> payload = new();

        payload.AddRange(BitConverter.GetBytes(peer.Network.ProtocolVersion));
        payload.AddRange(VarInt.GetBytes(locator.Count()));

        foreach (byte[] locatorHash in locator)
          payload.AddRange(locatorHash);

        payload.AddRange("0000000000000000000000000000000000000000000000000000000000000000".ToBinary());

        byte[] buffer = payload.ToArray();

        await peer.SendMessage(Command, buffer.Length, buffer);

        peer.Log($"Send getheaders. Locator: {locator.First().ToHexString()} ... {locator.Last().ToHexString()}");
      }

      public override string GetCommand()
      {
        return Command;
      }
    }
  }
}