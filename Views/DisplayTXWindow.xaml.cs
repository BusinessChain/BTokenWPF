using System;
using System.Collections.Generic;
using System.Windows;

using BTokenLib;

namespace BTokenWPF
{
  public partial class DisplayTXWindow : Window
  {
    public TX TX;

    public DisplayTXWindow(TX tX)
    {
      TX = tX;

      InitializeComponent();

      Title = $"TX: {tX}";

      List<(string label, string value)> labelValuePairs = tX.GetLabelsValuePairs();

      foreach((string label, string value) labelValuePair in labelValuePairs)
      {
        TextBoxTXLabels.Text += labelValuePair.label + "\n";
        TextBoxTXValues.Text += labelValuePair.value + "\n";
      }
    }

  }
}
