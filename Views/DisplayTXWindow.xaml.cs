using BTokenLib;
using System;
using System.Windows;

namespace BTokenWPF
{
  public partial class DisplayTXWindow : Window
  {
    public TX TX;

    public DisplayTXWindow(TX tX)
    {
      TX = tX;

      InitializeComponent();

      Title = $"Block: {tX.Hash.ToHexString()}";
    }

  }
}
