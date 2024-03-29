﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Security.Cryptography;


namespace BTokenLib
{
  public class TokenAnchor
  {
    public byte[] IDToken = new byte[2];
    public int NumberSequence;

    public byte[] HashBlockReferenced = new byte[32];
    public byte[] HashBlockPreviousReferenced = new byte[32];

    public TX TX;

    public override string ToString()
    {
      return TX.Hash.ToHexString();
    }
  }
}
