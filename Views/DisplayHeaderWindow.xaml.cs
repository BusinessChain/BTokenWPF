using BTokenLib;
using System;
using System.Windows;

namespace BTokenWPF
{
  public partial class DisplayHeaderWindow : Window
  {
    public Header Header;

    public DisplayHeaderWindow(Header header)
    {
      Header = header;

      InitializeComponent();

      Title = $"Block: {header.Hash.ToHexString()}";

      TextBoxHeaderLabels.Text =
        $"Hash\n" +
        $"Height\n" +
        $"Previous\n" +
        $"Next\n" +
        $"Parent\n" +
        $"Child\n" +
        $"MerkleRoot\n" +
        $"Count TXs\n" +
        $"Difficulty\n" +
        $"DifficultyAccumulated\n" +
        $"CountBytesBlock\n" +
        $"CountBytesBlocksAccumulated\n" +
        $"Timestamp\n" +
        $"Nonce\n";

      TextBoxHeaderValues.Text =
        $"{header.Hash.ToHexString()}\n" +
        $"{header.Height}\n" +
        $"{header.HashPrevious.ToHexString()}\n" +
        $"{header.HeaderNext}\n" +
        $"{header.HeaderParent}\n" +
        $"{header.HashChild.ToHexString()}\n" +
        $"{header.MerkleRoot.ToHexString()}\n" +
        $"{header.CountTXs}\n" +
        $"{header.Difficulty}\n" +
        $"{header.DifficultyAccumulated}\n" +
        $"{header.CountBytesBlock}\n" +
        $"{header.CountBytesBlocksAccumulated}\n" +
        $"{DateTimeOffset.FromUnixTimeSeconds(header.UnixTimeSeconds)}\n" +
        $"{header.Nonce}\n";
    }
  }
}
