using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using BTokenLib;

namespace BTokenWPF
{
  public partial class WindowToken : Window
  {
    public Token Token;

    public WindowToken(Token token)
    {
      InitializeComponent();

      Token = token;
      TextBoxToken.Text = token.GetName().Substring("Token".Count());

      UpdateControlsWindowToken();
    }

    async Task UpdateControlsWindowToken()
    {
      try
      {
        while (true)
        {
          if (Token.TryLock())
          {
            UpdateListBoxHeaderchain();

            UpdateTextBoxWallet();

            UpdateListBoxTXPool();

            UpdateListBoxDatabase();

            Token.ReleaseLock();
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

    Header HeaderTipAtLastUpdate;
    void UpdateTextBoxWallet()
    {
      if (HeaderTipAtLastUpdate == Token.HeaderTip)
        return;

      HeaderTipAtLastUpdate = Token.HeaderTip;

      ListBoxWallet.Items.Clear();
      ListBoxWallet.Items.Add(new ListBoxItemWallet());

      foreach (TXOutputWallet tXOutputWallet in Token.Wallet.OutputsValueDesc)
        ListBoxWallet.Items.Add(new ListBoxItemWallet(tXOutputWallet));
    }
    void UpdateListBoxTXPool()
    {
    }
    void UpdateListBoxDatabase()
    {
    }

    void UpdateListBoxHeaderchain()
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
    }

    private void ListBoxHeaderchain_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
      Header header = ((ListBoxItemHeader)ListBoxHeaderchain.SelectedItem).Header;

      foreach (Window w in Application.Current.Windows)
      {
        DisplayHeaderWindow windowDisplayHeader = w as DisplayHeaderWindow;
        if (windowDisplayHeader != null && windowDisplayHeader.Header == header)
        {
          windowDisplayHeader.Activate();
          return;
        }
      }

      new DisplayHeaderWindow(header).Show();
    }
  }
}
