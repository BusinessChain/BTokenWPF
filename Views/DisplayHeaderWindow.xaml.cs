﻿using BTokenLib;
using System;
using System.Windows;

namespace BTokenWPF
{
  public partial class DisplayHeaderWindow : Window
  {
    Token Token;
    public Header Header;

    public DisplayHeaderWindow(Header header, Token token)
    {
      Token = token;
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

      if (Token.TryGetBlockBytes(Header.Hash, out byte[] buffer))
      {
        Block block = Token.CreateBlock();
        block.Buffer = buffer;

        block.Parse();

        foreach(TX tX in  block.TXs)
        {
          ListBoxTXs.Items.Add(new ListBoxItemTX(tX));
        }
      }
      else
        MessageBox.Show("Block not found in archive, no transaction data shown.");
    }

    void ListBoxTXs_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
      TX tX = ((ListBoxItemTX)ListBoxTXs.SelectedItem).TX;

      foreach (Window w in Application.Current.Windows)
      {
        DisplayTXWindow windowDisplayTX = w as DisplayTXWindow;
        if (windowDisplayTX != null && windowDisplayTX.TX == tX)
        {
          windowDisplayTX.Activate();
          return;
        }
      }

      new DisplayTXWindow(tX).Show();
    }
  }
}
