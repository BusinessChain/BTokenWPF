using System;
using System.Windows;
using System.Windows.Controls;
using System.Threading.Tasks;

using BTokenLib;

namespace BTokenWPF
{
  public partial class WindowToken : Window
  {
    Token Token;

    public WindowToken(Token token)
    {
      InitializeComponent();

      Token = token;
      TextBoxToken.Text = token.GetName();

      PopulateListBoxHeaderchain();
    }

    async Task PopulateListBoxHeaderchain()
    {
      try
      {
        while (true)
        {
          Header header = null;

          if (ListBoxHeaderchain.Items.Count > 0)
            header = ((ListBoxItemHeader)ListBoxHeaderchain.Items.GetItemAt(0)).Header;

          if (Token.HeaderTip != header)
            if (ListBoxHeaderchain.Items.Count > 0 && Token.HeaderTip.HeaderPrevious == header)
              ListBoxHeaderchain.Items.Insert(0, new ListBoxItemHeader(Token.HeaderTip));
            else
            {
              ListBoxHeaderchain.Items.Clear();
              header = Token.HeaderTip;

              while (header != null)
              {
                ListBoxHeaderchain.Items.Add(new ListBoxItemHeader(header));
                header = header.HeaderPrevious;
              }
            }

          await Task.Delay(1000);
        }
      }
      catch (Exception ex)
      {
        MessageBox.Show(
          $"{ex.Message}: {ex.StackTrace}",
          $"{ex.GetType().Name}",
          MessageBoxButton.OK,
          MessageBoxImage.Error);
      }
    }

    private void ListBoxHeaderchain_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
      Header header = ((ListBoxItemHeader)ListBoxHeaderchain.SelectedItem).Header;

      new DisplayHeaderWindow(header).Show();
    }
  }
}
