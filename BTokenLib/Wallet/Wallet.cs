﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Diagnostics;
using System.Security.Cryptography;
using Org.BouncyCastle.Crypto.Digests;


namespace BTokenLib
{
  public partial class Wallet
  {
    const int HASH_BYTE_SIZE = 32;
    const int LENGTH_P2PKH = 25;
    byte[] PREFIX_P2PKH = new byte[] { 0x76, 0xA9, 0x14 };
    byte[] POSTFIX_P2PKH = new byte[] { 0x88, 0xAC };

    Crypto Crypto = new();

    SHA256 SHA256 = SHA256.Create();
    readonly RipeMD160Digest RIPEMD160 = new();

    string PrivKeyDec;
    byte[] PublicKey;
    byte[] PublicKeyHash160 = new byte[20];
    public byte[] PublicScript;

    List<TXOutputWallet> OutputsValueDesc = new();


    // Bitcoin Remote: 34028158965394806167608680607240385428045384897727389686353149816093835242965
    // Bitcoin Local: 102987336249554097029535212322581322789799900648198034993379397001115665086549
    // BToken Remote: 96776395506679815102655158894022185541899331171646085194829941027009356470640
    // BToken Local: 88359675621603939173265797657441078721738107517771649508403671881714167221003

    public Wallet(string privKeyDec)
    {
      PrivKeyDec = privKeyDec;

      PublicKey = Crypto.GetPubKeyFromPrivKey(PrivKeyDec);

      PublicKeyHash160 = ComputeHash160Pubkey(PublicKey);

      PublicScript = PREFIX_P2PKH
        .Concat(PublicKeyHash160)
        .Concat(POSTFIX_P2PKH).ToArray();
    }

    public byte[] ComputeHash160Pubkey(byte[] publicKey)
    {
      byte[] publicKeyHash160 = new byte[20];

      var hashPublicKey = SHA256.ComputeHash(publicKey);
      RIPEMD160.BlockUpdate(hashPublicKey, 0, hashPublicKey.Length);
      RIPEMD160.DoFinal(publicKeyHash160, 0);

      return publicKeyHash160;
    }

    public void DetectTXOutputSpendable(TX tX, TXOutput tXOutput)
    {
      if (tXOutput.LengthScript != LENGTH_P2PKH)
        return;

      int indexScript = tXOutput.StartIndexScript;

      if (!PREFIX_P2PKH.IsEqual(tXOutput.Buffer, indexScript))
        return;

      indexScript += 3;

      if (!PublicKeyHash160.IsEqual(tXOutput.Buffer, indexScript))
        return;

      indexScript += 20;

      if (POSTFIX_P2PKH.IsEqual(
        tXOutput.Buffer,
        indexScript))
      {
        byte[] scriptPubKey = new byte[LENGTH_P2PKH];

        Array.Copy(
          tXOutput.Buffer,
          tXOutput.StartIndexScript,
          scriptPubKey,
          0,
          LENGTH_P2PKH);

        AddOutput(
          new TXOutputWallet
          {
            TXID = tX.Hash,
            TXIDShort = tX.TXIDShort,
            Index = tX.TXOutputs.IndexOf(tXOutput),
            Value = tXOutput.Value
          });
      }
    }

    public List<byte> GetScriptSignature(byte[] tXRaw)
    {
      byte[] signature = Crypto.GetSignature(
      PrivKeyDec,
      tXRaw.ToArray());

      List<byte> scriptSig = new();
      scriptSig.Add((byte)(signature.Length + 1));
      scriptSig.AddRange(signature);
      scriptSig.Add(0x01);

      scriptSig.Add((byte)PublicKey.Length);
      scriptSig.AddRange(PublicKey);

      return scriptSig;
    }

    public bool TrySpend(TXInput tXInput)
    {
      TXOutputWallet output =
        OutputsValueDesc.Find(o =>
        o.TXID.IsEqual(tXInput.TXIDOutput) &&
        o.Index == tXInput.OutputIndex);

      if (output == null)
        return false;

      OutputsValueDesc.Remove(output);

      return true;
    }

    public void RemoveOutput(byte[] hash)
    {
      int i = OutputsValueDesc.RemoveAll(t => t.TXID.Equals(hash));
    }

    public void AddOutput(TXOutputWallet output)
    {
      if (OutputsValueDesc.Any(
        o => o.TXID.IsEqual(output.TXID) && 
        o.Index == output.Index))
        return;

      int j = 0;

      while(j < OutputsValueDesc.Count && output.Value < OutputsValueDesc[j].Value)
        j += 1;

      OutputsValueDesc.Insert(j, output);
    }

    public bool TryGetOutput(
      long fee,
      out TXOutputWallet tXOutputWallet)
    {
      tXOutputWallet = null;

      if (OutputsValueDesc.Any() && 
        OutputsValueDesc[0].Value > fee)
      {
        tXOutputWallet = OutputsValueDesc[0];
        OutputsValueDesc.RemoveAt(0);
      }

      return tXOutputWallet != null;
    }
    
    public byte[] GetReceptionScript()
    {
      byte[] script = new byte[26];

      script[0] = LENGTH_P2PKH;

      PREFIX_P2PKH.CopyTo(script, 1);
      PublicKeyHash160.CopyTo(script, 4);
      POSTFIX_P2PKH.CopyTo(script, 24);

      return script;
    }

    public string GetStatus()
    {
      string outputsSpendable =
        OutputsValueDesc.Any() ? "" : "Wallet empty.";

      foreach (var output in OutputsValueDesc)
      {
        outputsSpendable += $"TXID: {output.TXID.ToHexString()}\n";
        outputsSpendable += $"Output Index: {output.Index}\n";
        outputsSpendable += $"Value: {output.Value}\n";
      }

      return outputsSpendable;
    }

    public void Clear()
    {
      OutputsValueDesc.Clear();
    }

    public void LoadImage(string pathImage)
    {
      string pathFile = Path.Combine(
        pathImage, "ImageWallet");

      int index = 0;

      byte[] buffer = File.ReadAllBytes(pathFile);

      while (index < buffer.Length)
      {
        var tXOutput = new TXOutputWallet();

        tXOutput.TXID = new byte[HASH_BYTE_SIZE];
        Array.Copy(buffer, index, tXOutput.TXID, 0, HASH_BYTE_SIZE);
        index += HASH_BYTE_SIZE;

        tXOutput.TXIDShort = BitConverter.ToInt32(tXOutput.TXID, 0);

        tXOutput.Index = BitConverter.ToInt32(buffer, index);
        index += 4;

        tXOutput.Value = BitConverter.ToInt64(buffer, index);
        index += 8;

        AddOutput(tXOutput);
      }
    }

    public void CreateImage(string pathDirectory)
    {
      string pathimageWallet = Path.Combine(
         pathDirectory,
         "ImageWallet");

      using (var fileImageWallet =
        new FileStream(
          pathimageWallet,
          FileMode.Create,
          FileAccess.Write,
          FileShare.None))
      {
        foreach (TXOutputWallet tXOutput in OutputsValueDesc)
        {
          fileImageWallet.Write(
            tXOutput.TXID, 0, tXOutput.TXID.Length);


          byte[] outputIndex = BitConverter.GetBytes(
            tXOutput.Index);

          fileImageWallet.Write(
            outputIndex, 0, outputIndex.Length);


          byte[] value = BitConverter.GetBytes(
            tXOutput.Value);

          fileImageWallet.Write(
            value, 0, value.Length);
        }
      }
    }
  }
}