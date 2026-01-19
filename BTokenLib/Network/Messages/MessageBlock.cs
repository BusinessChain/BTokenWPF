using System;


namespace BTokenLib
{
  partial class Network
  {
    partial class Peer
    {
      class MessageBlock : MessageNetwork
      {

        public MessageBlock()
          : base("block") { }

        public MessageBlock(byte[] bufferBlock, int lengthPayload)
          : base(
              "block",
              bufferBlock,
              0,
              lengthPayload)
        { }

        public override MessageNetwork Create()
        {
          return new MessageBlock();
        }

        public override void RunMessage(Peer peer)
        {

        }
      }
    }
  }
}