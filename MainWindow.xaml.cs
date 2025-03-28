using System;
using System.IO;
using System.Windows;
using System.Threading.Tasks;

using BTokenLib;


namespace BTokenWPF
{
  public partial class MainWindow : Window, ILogEntryNotifier
  {
    TokenBToken BToken;

    public MainWindow()
    {
      try
      {
        InitializeComponent();

        byte[] iDToken = new byte[4] { (byte)'B', (byte)'C', (byte)'S', (byte)'H' };

        TokenBitcoin tokenBitcoin = new(this);

        BToken = new(this, iDToken, port: 8777, tokenBitcoin);
        BToken.Start();

        UpdateTextBoxStatus();
      }
      catch(Exception ex)
      {
        MessageBox.Show(
          $"{ex.Message}: {ex.StackTrace}",
          $"{ex.GetType().Name}",
          MessageBoxButton.OK,
          MessageBoxImage.Error);
      }
    }

    async Task UpdateTextBoxStatus()
    {
      try
      {
        while (true)
        {
          await Task.Delay(1000);
          LabelBitcoinStatus.Content = BToken.TokenParent.GetStatus();
          LabelBTokenStatus.Content = BToken.GetStatus();
        }
      }
      catch(Exception ex)
      {
        MessageBox.Show(
          $"{ex.Message}: {ex.StackTrace}",
          $"{ex.GetType().Name}",
          MessageBoxButton.OK,
          MessageBoxImage.Error);
      }
    }

    bool FlagTextBoxLogFreezed;
    readonly object LOCK_Dispatcher = new();
    public void NotifyLogEntry(string logEntry, string source)
    {
      if (FlagTextBoxLogFreezed)
        return;

      lock (LOCK_Dispatcher)
        Dispatcher.Invoke(() =>
        {
          TextBoxLog.SelectionStart = 0;
          TextBoxLog.SelectionLength = 0;
          TextBoxLog.SelectedText = $"{logEntry}\n";
        });
    }

    void ButtonClearTextBoxLog_Click(object sender, RoutedEventArgs e)
    {
      TextBoxLog.Clear();
    }

    void ButtonFreezeTextBoxLog_Click(object sender, RoutedEventArgs e)
    {
      if(FlagTextBoxLogFreezed)
        ButtonFreezeLog.Content = "Freeze Log";
      else
        ButtonFreezeLog.Content = "Resume Log";

      FlagTextBoxLogFreezed = !FlagTextBoxLogFreezed;
    }

    void ButtonBitcoinMiner_Click(object sender, RoutedEventArgs e)
    {
      if (BToken.TokenParent.IsMining)
      {
        BToken.TokenParent.StopMining();
        ButtonStartBitcoinMiner.Content = "Start BitcoinMiner";
      }
      else
      {
        BToken.TokenParent.StartMining();
        ButtonStartBitcoinMiner.Content = "Stop BitcoinMiner";
      }
    }

    void ButtonBTokenMiner_Click(object sender, RoutedEventArgs e)
    {
      if (BToken.IsMining)
      {
        BToken.StopMining();
        ButtonStartBTokenMiner.Content = "Start BTokenMiner";
      }
      else
      {
        BToken.StartMining();
        ButtonStartBTokenMiner.Content = "Stop BTokenMiner";
      }
    }

    void ButtonStartSynchronizationNode_Click(object sender, RoutedEventArgs e)
    {
      BToken.StartSync();
    }

    void ButtonOpenBitcoinWindow_Click(object sender, RoutedEventArgs e)
    {
      OpenWindowToken(BToken.TokenParent);
    }

    void ButtonOpenBTokenWindow_Click(object sender, RoutedEventArgs e)
    {
      OpenWindowToken(BToken);
    }

    void OpenWindowToken(Token token)
    {
      foreach (Window w in Application.Current.Windows)
      {
        WindowToken windowToken = w as WindowToken;
        if (windowToken != null && windowToken.Token == token)
        {
          windowToken.Activate();
          return;
        }
      }

      new WindowToken(token).Show();
    }

  }
}
