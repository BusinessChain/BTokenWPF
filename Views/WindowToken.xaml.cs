using System;
using System.Linq;
using System.Windows;
using BTokenWPF.Views;
using System.Threading.Tasks;
using System.Collections.Generic;

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
      LabelToken.Content = token.GetName().Substring("Token".Count());
      TextBoxAccount.Text = token.Wallet.AddressAccount;

      UpdateControlsWindowToken();
    }

    async Task UpdateControlsWindowToken()
    {
      try
      {
        CheckBoxEnableOutboundConnections.IsChecked = Token.Network.FlagEnableOutboundConnections;

        while (true)
        {
          if (Token.TryLock())
          {
            UpdateListBoxHeaderchain();

            UpdateTextBoxWallet();

            UpdateListBoxTXPool();

            Token.ReleaseLock();
          }

          UpdateControlsNetwork();

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

    void UpdateControlsNetwork()
    {
      List<Network.Peer> peers = Token.Network.GetPeers();

      LabelCountPeers.Content = $"Number of peers: {peers.Count}";
      LabelStateNetworkConnector.Content = $"State network connector: {Token.Network.State}";
      LabelIsStateSynchronizing.Content = $"Network synchronizing: {Token.Network.IsStateSynchronizing}";
      Token.Network.FlagEnableOutboundConnections = (bool)CheckBoxEnableOutboundConnections.IsChecked;

      ListBoxPeers.Items.Clear();

      foreach (Network.Peer peer in peers)
        ListBoxPeers.Items.Add(new ListBoxItemPeer(peer));
    }

    void UpdateTextBoxWallet()
    {
      TextBoxBalanceSatoshies.Text = Token.Wallet.GetBalance().ToString();

      ListBoxWallet.Items.Clear();
      ListBoxWallet.Items.Add(new ListBoxItemWallet());

      if (Token.Wallet is WalletBitcoin)
      {
        foreach (TXOutputWallet tXOutputWallet in ((WalletBitcoin)Token.Wallet).OutputsSpendable)
          if (!Token.Wallet.OutputsSpentUnconfirmed.Contains(tXOutputWallet))
            ListBoxWallet.Items.Add(new ListBoxItemWallet(tXOutputWallet, "confirmed"));

        foreach (TXOutputWallet tXOutputWallet in Token.Wallet.OutputsUnconfirmed)
          if (!Token.Wallet.OutputsSpentUnconfirmed.Contains(tXOutputWallet))
            ListBoxWallet.Items.Add(new ListBoxItemWallet(tXOutputWallet, "unconfirmed"));
      }
    }

    void UpdateListBoxTXPool()
    {
      List<TX> tXs = Token.TXPool.GetTXs(countMax: int.MaxValue, out long feeTXs);

      ListBoxTXPool.Items.Clear();

      foreach (TX tX in tXs)
        ListBoxTXPool.Items.Add(new ListBoxItemTX(tX));
    }

    void ListBoxTXPool_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
      TX tX = ((ListBoxItemTX)ListBoxTXPool.SelectedItem).TX;

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

    void UpdateListBoxHeaderchain()
    {
      Header header = null;

      if (ListBoxBlockchain.Items.Count > 0)
        header = ((ListBoxItemHeader)ListBoxBlockchain.Items.GetItemAt(0)).Header;

      if (Token.HeaderTip != header)
        if (ListBoxBlockchain.Items.Count > 0 && Token.HeaderTip.HeaderPrevious == header)
          ListBoxBlockchain.Items.Insert(0, new ListBoxItemHeader(Token.HeaderTip));
        else
        {
          ListBoxBlockchain.Items.Clear();
          header = Token.HeaderTip;

          while (header != null)
          {
            ListBoxBlockchain.Items.Add(new ListBoxItemHeader(header));
            header = header.HeaderPrevious;
          }
        }
    }

    void ListBoxBlockchain_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
      Header header = ((ListBoxItemHeader)ListBoxBlockchain.SelectedItem).Header;

      foreach (Window w in Application.Current.Windows)
      {
        DisplayHeaderWindow windowDisplayHeader = w as DisplayHeaderWindow;
        if (windowDisplayHeader != null && windowDisplayHeader.Header == header)
        {
          windowDisplayHeader.Activate();
          return;
        }
      }

      new DisplayHeaderWindow(header, Token).Show();
    }

    void ButtonMakeTX_Click(object sender, RoutedEventArgs e)
    {
      try
      {
        string address = TextBoxAddress.Text;
        long value = long.Parse(TextBoxValue.Text);
        double fee = double.Parse(TextBoxFee.Text);

        if (Token.TrySendTX(address, value, fee, out TX tX))
        {
          TextBoxRawTX.Text = tX.TXRaw.ToArray().Reverse().ToArray().ToHexString();
          TextBoxTXID.Text = tX.Hash.ToHexString().ToLower();
        }
        else
          MessageBox.Show(
            "Could not send tX. Possibly not enough fund.",
            "Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
      }
      catch(Exception ex)
      {
        MessageBox.Show(
          ex.Message, 
          "Exception", 
          MessageBoxButton.OK, 
          MessageBoxImage.Error);
      }
    }
  }
}
