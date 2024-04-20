using System;
using System.Collections.Generic;
using System.Formats.Asn1;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace BTokenLib
{
  public class TXInput
  {
    const int HASH_BYTE_SIZE = 32;

    public byte[] TXIDOutput;
    public int OutputIndex;

    public int Sequence;


    public TXInput(Stream stream)
    {
      TXIDOutput = new byte[HASH_BYTE_SIZE];

      stream.Read(TXIDOutput, 0, HASH_BYTE_SIZE);

      OutputIndex = stream.ReadInt32();

      int lengthScript = VarInt.GetInt(stream);

      stream.Position += lengthScript;

      Sequence = stream.ReadInt32();
    }
  }
}
