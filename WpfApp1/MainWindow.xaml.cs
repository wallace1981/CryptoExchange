using System;
using System.Linq;
using System.Net;
using System.Windows;
using Telegram.Bot;
using Telegram.Bot.Args;
using Exchange.Net;

namespace WpfApp1
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            //WorkWithTelegram();

            var go = new xceed.TestViewModel("GOBTC");
            var gnt = new xceed.TestViewModel("GNTBTC");
            var appc = new xceed.TestViewModel("APPCBTC");
            var snm = new xceed.TestViewModel("SNMBTC");
            var qlc = new xceed.TestViewModel("QLCBTC");
            var btc = new xceed.TestViewModel("BTCUSDT");

            dock.Children.Add(new xceed.Trades() { DataContext = btc });
            dock.Children.Add(new xceed.Trades() { DataContext = appc });
            dock.Children.Add(new xceed.Trades() { DataContext = go });
            dock.Children.Add(new xceed.Trades() { DataContext = gnt });
            dock.Children.Add(new xceed.Trades() { DataContext = snm });
            dock.Children.Add(new xceed.Trades() { DataContext = qlc });
        }

        public void Finish()
        {
            foreach (xceed.TestViewModel vm in dock.Children.Cast<FrameworkElement>().Select(x => x.DataContext))
            {
                vm.FinishImpl();
            }
        }

        private async void WorkWithTelegram()
        {
            WebProxy wp = new WebProxy("http://193.70.41.31:8080", true);
            client = new TelegramBotClient("690037046:AAGnCVx8mpG0QexUi-tIcMyFJzjprlr_wJE", wp);
            var me = await client.GetMeAsync();

            client.OnMessage += Client_OnMessage;
            client.OnUpdate += Client_OnUpdate;
            client.StartReceiving();
        }

        private void Client_OnUpdate(object sender, UpdateEventArgs e)
        {
            switch (e.Update.Type)
            {
                case Telegram.Bot.Types.Enums.UpdateType.Message:
                case Telegram.Bot.Types.Enums.UpdateType.EditedMessage:
                    {
                        if (e.Update.Message.Text == "/start")
                        {
                            client.SendTextMessageAsync(chatId: e.Update.Message.Chat, text: $"Your chat id is: {e.Update.Message.Chat.Id}");
                            client.SendTextMessageAsync(new Telegram.Bot.Types.ChatId(542344060), text: $"Added chat id: {e.Update.Message.Chat.Id}");
                        }
                        break;
                    }
                case Telegram.Bot.Types.Enums.UpdateType.ChannelPost:
                case Telegram.Bot.Types.Enums.UpdateType.EditedChannelPost:
                    {
                        var post = e.Update.ChannelPost;
                        var from = post.Chat.Title;
                        client.SendTextMessageAsync(new Telegram.Bot.Types.ChatId(542344060), text: string.Join(Environment.NewLine, from, e.Update.ChannelPost.Text));
                        break;
                    }
            }
        }

        private void Client_OnMessage(object sender, MessageEventArgs e)
        {
            if (e.Message.Text != null)
            {
                if (e.Message.Text == "/start")
                {
                    client.SendTextMessageAsync(chatId: e.Message.Chat, text: $"Your chat id is: {e.Message.Chat.Id}");
                }
                else if (e.Message.Text.StartsWith("/newsignal"))
                {
                    var formattedSignal = ProcessSignal(e.Message.Text.Replace("/newsignal", string.Empty));
                    client.SendTextMessageAsync(chatId: e.Message.Chat, text: formattedSignal, parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown);
                }
            }
        }

        //  BINANCE:MTH/BTC
        //  BUY:0.0001513
        //  SL:0.0001235
        //  TP:0.0001570,0.0001610,0.0001650,0.0001710
        //  VOL:0.1 // 10%
        private string ProcessSignal(string v)
        {
            string[] lines = v.Trim().Split(new [] { '\r', '\n' },  StringSplitOptions.RemoveEmptyEntries);
            string exchange, market;
            decimal buy = 0, sl = 0, amount = 0;
            decimal[] tp = null;

            // 1st line should be exchange:market
            var parts = lines[0].Split(new[] { ':' });
            exchange = parts[0];
            market = parts[1];

            for (int ind = 1; ind < lines.Length; ++ind)
            {
                parts = lines[ind].Split(new[] { ':' });
                switch (parts[0].ToUpper())
                {
                    case "BUY":
                        decimal.TryParse(parts[1].Trim(), out buy);
                        break;
                    case "SL":
                        decimal.TryParse(parts[1].Trim(), out sl);
                        break;
                    case "VOL":
                        decimal.TryParse(parts[1].Trim(), out amount);
                        break;
                    case "TP":
                        parts = parts[1].Trim().Split(new[] { ',' });
                        tp = parts.Select(x => decimal.Parse(x.Trim())).ToArray();
                        break;
                }
            }

            string humanReadable = string.Empty;
            humanReadable += $"#{market}" + Environment.NewLine + Environment.NewLine;
            humanReadable += $"Покупка {buy} и ниже" + Environment.NewLine + Environment.NewLine;
            humanReadable += $"Цели:" + Environment.NewLine;
            foreach (var x in tp)
            {
                humanReadable += $"{x}" + Environment.NewLine;
            }
            humanReadable += Environment.NewLine;
            humanReadable += $"Стоп ставим на {sl}" + Environment.NewLine + Environment.NewLine;
            humanReadable += $"*Максимум {amount:P0} депозита*";

            return humanReadable;
        }

        ITelegramBotClient client;

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            Finish();
        }
    }
}
