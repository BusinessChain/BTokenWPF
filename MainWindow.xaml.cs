using BTokenLib;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace BTokenWPF
{
  public partial class MainWindow : Window, BTokenLib.ILogEntryNotifier
  {
    BTokenLib.TokenBToken BToken;

    public MainWindow()
    {
      try
      {
        InitializeComponent();

        BToken = new(this);

        new Thread(BToken.Start).Start();

        UpdateTextBoxStatus();
      }
      catch(Exception ex)
      {
        Debug.WriteLine($"{ex.GetType().Name} on line 25:\n {ex.Message}");
      }
    }

    async Task UpdateTextBoxStatus()
    {
      try
      {
        while (true)
        {
          await Task.Delay(1000);
          TextBoxBitcoinStatus.Text = BToken.TokenParent.GetStatus();
          TextBoxBTokenStatus.Text = BToken.GetStatus();
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

    readonly object LOCK_Dispatcher = new();
    public void NotifyLogEntry(string logEntry, string source)
    {
      lock (LOCK_Dispatcher)
        Dispatcher.Invoke(() =>
        {
          TextBoxLog.AppendText($"{logEntry}\n");
        });
    }

    void ButtonClearTextBoxLog_Click(object sender, RoutedEventArgs e)
    {
      TextBoxLog.Text = "";
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
