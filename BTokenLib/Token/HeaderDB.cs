using System;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using System.Security.Cryptography;

using LiteDB;


namespace BTokenLib
{
  public class HeaderDB
  {
    [BsonId]
    public byte[] Hash;

    public byte[] Data;
  }
}
