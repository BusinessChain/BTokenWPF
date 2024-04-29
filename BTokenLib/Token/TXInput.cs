﻿using System;
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


    public TXInput()
    { }


    public TXInput(byte[] buffer, ref int index)
    {
      TXIDOutput = new byte[HASH_BYTE_SIZE];

      Array.Copy(buffer, index, TXIDOutput, 0, HASH_BYTE_SIZE);
      index += HASH_BYTE_SIZE;

      OutputIndex = BitConverter.ToInt32(buffer, index);
      index += 4;

      int lengthScript = VarInt.GetInt(buffer, ref index);
      index += lengthScript;

      Sequence = BitConverter.ToInt32(buffer, index);
      index += 4;
    }

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
