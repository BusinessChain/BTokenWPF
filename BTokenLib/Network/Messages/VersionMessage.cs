using System;
using System.Collections.Generic;
using System.Net;

namespace BTokenLib
{
  partial class Network
  {
    class VersionMessage : MessageNetworkProtocol
    {
      public const string Command = "version";

      public const UInt32 ProtocolVersion = 70015;


      public VersionMessage()
      { }

      public VersionMessage(
        ulong networkServicesLocal,
        long unixTimeSeconds,
        ulong networkServicesRemote,
        IPAddress iPAddressRemote,
        int portRemote,
        IPAddress iPAddressLocal,
        int portLocal,
        ulong nonce,
        string userAgent,
        int blockchainHeight,
        byte relayOption)
      {
        List<byte> versionPayload = new();

        versionPayload.AddRange(BitConverter.GetBytes(ProtocolVersion));
        versionPayload.AddRange(BitConverter.GetBytes(networkServicesLocal));
        versionPayload.AddRange(BitConverter.GetBytes(unixTimeSeconds));
        versionPayload.AddRange(BitConverter.GetBytes(networkServicesRemote));
        versionPayload.AddRange(iPAddressRemote.GetAddressBytes());
        versionPayload.AddRange(GetBytes((ushort)portRemote));
        versionPayload.AddRange(BitConverter.GetBytes(networkServicesLocal));
        versionPayload.AddRange(iPAddressLocal.GetAddressBytes());
        versionPayload.AddRange(GetBytes((ushort)portLocal));
        versionPayload.AddRange(BitConverter.GetBytes(nonce));
        versionPayload.AddRange(VarString.GetBytes(userAgent));
        versionPayload.AddRange(BitConverter.GetBytes(blockchainHeight));
        versionPayload.Add(relayOption);

        Payload = versionPayload.ToArray();
        LengthDataPayload = Payload.Length;
      }

      byte[] GetBytes(UInt16 uint16)
      {
        byte[] byteArray = BitConverter.GetBytes(uint16);
        Array.Reverse(byteArray);
        return byteArray;
      }

      public override string GetCommand()
      {
        return Command;
      }
    }
  }
}
