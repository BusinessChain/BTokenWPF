using BTokenWPF;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Linq;


namespace BTokenLib
{
  public partial class TokenBitcoin : Token
  {
    const UInt16 COMPORT_BITCOIN = 8333;

    PoolTXBitcoin TXPool = new();


    public TokenBitcoin(ILogEntryNotifier logEntryNotifier)
      : base(
          COMPORT_BITCOIN,
          iDToken: new byte[TokenAnchor.LENGTH_IDTOKEN],
          flagEnableInboundConnections: true,
          logEntryNotifier)
    {
      Wallet = new WalletBitcoin(
        File.ReadAllText($"Wallet{GetName()}/wallet"), 
        this);
    }

    public override Header CreateHeaderGenesis()
    {
      //HeaderBitcoin header = new(
      //   headerHash: "0000000000000000000230d9bb1db81e56916b0c2c7363231e75b82b24714482".ToBinary(),
      //   version: 0x01,
      //   hashPrevious: "00000000000000000008b5ffa0ae1b604dd27bf4af84602ea53f7920320a3c96".ToBinary(),
      //   merkleRootHash: "ef303d1cf8090e1bcea36432eceea2bbc156e81108deff1616d9c6dee64ba7c7".ToBinary(),
      //   unixTimeSeconds: 1653490985, // take timestamp from trezor.io explorer and convert to epoch time GMT
      //   nBits: 386492960,
      //   nonce: 578608666);

      //header.Height = 737856; // Should be modulo 2016 so it calculates next target bits correctly.

      HeaderBitcoin header = new HeaderBitcoin(
         headerHash: "000000A13F15EC9FECECAB8EF438F8E16E729AC2AF816C3DBE7E27BAF110F66A".ToBinary(),
         version: 0x01,
         hashPrevious: "0000000000000000000000000000000000000000000000000000000000000000".ToBinary(),
         merkleRootHash: "0000000000000000000000000000000000000000000000000000000000000000".ToBinary(),
         unixTimeSeconds: 1667333891,
         //nBits: 0x1d4fffff,
         nBits: 0x1dffffff,
         nonce: 1441757173);

      header.Height = 0; // Should be modulo 2016 so it calculates next target bits correctly.

      header.DifficultyAccumulated = header.Difficulty;

      return header;
    }

    public override Block CreateBlock()
    {
      return new BlockBitcoin(this);
    }

    public override TX ParseTX(
      Stream stream,
      SHA256 sHA256,
      bool flagCoinbase)
    {
      TXBitcoin tX = new();

      try
      {
        long tXStartIndex = stream.Position;

        stream.Position += 4; // Version

        int countInputs = VarInt.GetInt(stream);

        if (countInputs == 0x00)
          throw new NotImplementedException("Segwit is not implemented.");

        for (int i = 0; i < countInputs; i += 1)
          tX.Inputs.Add(new TXInput(stream));

        int countTXOutputs = VarInt.GetInt(stream);

        for (int i = 0; i < countTXOutputs; i += 1)
        {
          TXOutputBitcoin tXOutputBitcoin = new(stream);
          tX.TXOutputs.Add(tXOutputBitcoin);

          if (i == 0 && tXOutputBitcoin.Type == TXOutputBitcoin.TypesToken.AnchorToken)
            tXOutputBitcoin.TokenAnchor.TX = tX;
        }

        stream.Position += 4; //BYTE_LENGTH_LOCK_TIME

        long tXRawLength = stream.Position - tXStartIndex;

        byte[] tXRaw = new byte[tXRawLength];
        stream.Position = tXStartIndex;
        stream.Read(tXRaw, 0, tXRaw.Length);

        tX.TXRaw = tXRaw.ToList();

        tX.Hash = sHA256.ComputeHash(
         sHA256.ComputeHash(tXRaw, 0, tXRaw.Length));

        return tX;
      }
      catch (ArgumentOutOfRangeException)
      {
        throw new ProtocolException(
          "ArgumentOutOfRangeException thrown in ParseTX.");
      }
    }

    protected override void InsertInDatabase(Block block)
    {
      WalletBitcoin walletBitcoin = (WalletBitcoin)Wallet;

      foreach (TXBitcoin tX in block.TXs)
      {
        walletBitcoin.InsertTX(tX);

        TokenAnchor tokenAnchor = tX.TXOutputs[0].TokenAnchor;

        if (tokenAnchor != null)
        {
          Token tokenChild = TokensChild.Find(
            t => t.IDToken.IsEqual(tokenAnchor.IDToken));

          if (tokenChild != null)
            tokenChild.SignalAnchorTokenDetected(tokenAnchor);
        }
      }

      TXPool.RemoveTXs(block.TXs.Select(tX => tX.Hash));
    }

    public override List<string> GetSeedAddresses()
    {
      return new List<string>()
        {"83.229.86.158"
            //"167.179.147.155","95.89.103.28","2.59.236.56", "49.64.10.128", "91.219.25.232",
            //"3.8.174.255", "93.216.78.178", "88.99.209.7", "93.104.126.120", "47.149.50.194",
            //"18.183.139.213", "49.64.10.100", "49.12.82.82", "3.249.250.35", "86.220.37.55",
            //"147.194.177.165", "5.9.42.21", "75.56.8.205","86.166.110.213","35.201.215.214",
            //"88.70.152.28", "97.84.96.62", "185.180.196.74","34.101.105.12", "77.21.236.207",
            //"93.177.82.226", "51.75.61.18", "51.75.144.201", "185.46.17.66", "50.98.185.178",
            //"31.14.40.64", "185.216.178.92", "173.230.133.14", "50.39.164.136", "13.126.144.12",
            //"149.90.214.78", "66.208.64.128", "37.235.134.102", "18.141.198.180", "62.107.200.30",
            //"162.0.216.227", "85.10.206.119", "95.164.65.194", "35.196.81.199", "85.243.55.37",
            //"167.172.151.136", "86.89.77.44", "221.140.248.61", "62.171.166.70", "90.146.130.214",
            //"70.183.190.131", "84.39.176.10", "89.33.195.97", "165.22.224.124", "87.220.77.134",
            //"141.94.74.233", "73.108.241.200", "73.108.241.200", "87.184.110.132", "34.123.171.121",
            //"85.149.70.74", "167.172.41.211", "85.165.8.197", "157.90.133.235", "185.73.200.134",
            //"68.37.223.44", "79.98.159.7", "79.98.159.7", "63.224.37.22", "94.23.248.168",
            //"195.213.137.231", "3.248.215.13", "195.201.56.56", "51.210.61.169", "5.166.54.83",
            //"3.137.140.0", "3.17.174.43", "84.112.177.143", "173.249.0.235", "178.63.52.122",
            //"3.112.22.239", "168.119.249.19", "162.55.3.214", "194.147.113.201", "5.9.99.119",
            //"209.133.222.18", "217.20.194.197", "3.248.223.135", "165.22.224.124", "45.88.188.220",
            //"188.166.120.198", "5.128.87.126", "5.9.2.199", "185.85.3.140", "3.219.41.205",
            //"185.25.48.7", "62.210.123.142", "15.237.117.105", "82.71.47.216", "161.97.84.78",
            //"86.9.128.254","134.209.74.26", "35.181.8.232", "168.119.18.7", "144.76.1.155",
            //"78.129.201.15", "65.108.70.139", "51.210.208.70", "79.137.68.161", "85.241.35.244",
            //"34.106.217.38", "192.222.24.54", "165.22.235.29", "18.221.170.34", "23.95.207.214",
            //"138.201.56.226", "193.234.50.227", "5.254.56.74", "213.214.66.182", "95.211.152.100",
            //"84.92.92.247", "169.0.168.95", "178.164.201.40", "190.2.149.82", "195.214.133.15",
            //"199.247.227.180", "201.210.177.231", "207.191.102.93", "212.93.114.22", "45.183.140.233",
            //"47.198.204.108", "81.251.223.139", "84.85.227.8", "88.207.124.229", "91.56.252.34",
            //"92.116.38.250", "95.56.63.179", "97.115.103.114", "98.143.78.109", "98.194.50.84",
            //"134.19.118.183", "152.169.255.224",
        };
    }

    public override void CreateImageDatabase(string pathDirectory)
    { }

    public override void LoadImageDatabase(string path)
    { }

    public override void AddTXToPool(TX tX)
    {
      TXBitcoin tXBitcoin = (TXBitcoin)tX;

      if (!TXPool.TryAddTX(tXBitcoin))
        throw new ProtocolException($"Could not insert tX {tX} to Bitcoin pool.");

      ((WalletBitcoin)Wallet).InsertTXUnconfirmed(tXBitcoin);
    }

    public override bool TryGetFromTXPool(byte[] hashTX, out TX tX)
    {
      bool flagSuccess = TXPool.TryGetTX(hashTX, out TXBitcoin tXBitcoin);

      tX = tXBitcoin;

      return flagSuccess;
    }

    public override void RBFAnchorTokens(
      ref List<TokenAnchor> tokensAnchorSelfMinedUnconfirmed, 
      TokenAnchor tokenAnchorTemplate)
    {
      List<TXBitcoin> tXAnchorTokens = new();

      foreach (TokenAnchor tokenAnchor in tokensAnchorSelfMinedUnconfirmed)
      {
        TXPool.TryGetTX(tokenAnchor.TX.Hash, out TXBitcoin tX);

        if(tXAnchorTokens.Count == 0)
          tXAnchorTokens.Add(tX);
        else if (tX.Inputs.Count > 0)
        {
          if (tXAnchorTokens[0].Inputs.Count > 0)
            throw new ProtocolException(
              $"Only one anchorToken in RBF graph should have more than one input,\n" +
              $"but there are {tXAnchorTokens[0]} and {tX}.");

          tXAnchorTokens.Insert(0, tX);
        }
        else
          for (int i = 0; i < tXAnchorTokens.Count; i += 1)
            if (tXAnchorTokens[i].Hash.IsEqual(tX.Inputs[0].TXIDOutput))
              tXAnchorTokens.Insert(i + 1, tX);
      }

      for (int i = 0; i < tXAnchorTokens.Count - 1; i += 1)
        if (!tXAnchorTokens[i].Hash.IsEqual(tXAnchorTokens[i + 1].Inputs[0].TXIDOutput))
          throw new ProtocolException(
            $"RBF Anchor tokens do not build a coherent graph:" +
            $"\nAnchor tX {tXAnchorTokens[i + 1]} does not reference {tXAnchorTokens[i]} but should.");

      TXPool.RemoveTXRecursive(tXAnchorTokens[0].Hash);

      ((WalletBitcoin)Wallet).ReverseTXsUnconfirmed(tXAnchorTokens);

      tokensAnchorSelfMinedUnconfirmed.Clear();
      for (int i = 0; i < tXAnchorTokens.Count; i += 1)
      {
        TokenAnchor tokenAnchor = tokenAnchorTemplate.Copy();

        if (TryBroadcastAnchorToken(tokenAnchor))
          tokensAnchorSelfMinedUnconfirmed.Add(tokenAnchor);
        else
          break;
      }
    }
  }
}
