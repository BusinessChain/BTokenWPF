﻿using BTokenLib;
using System.Windows.Controls;

namespace BTokenWPF
{
  public partial class ListBoxItemTX : UserControl
  {
    TX TX;

    public ListBoxItemTX(TX tX)
    {
      TX = tX;

      InitializeComponent();

      LabelTXHash.Content = tX.Hash.ToHexString();
      LabelTXRaw.Content = tX.TXRaw.ToArray().ToHexString();

      LabelCountInputs.Content = $"Number of Inputs: {tX.TXInputs.Count}";
      LabelCountOutputs.Content = $"Number of Outputs: {tX.TXOutputs.Count}";
      LabelFee.Content = $"Fee: {tX.Fee}";
    }
  }
}
