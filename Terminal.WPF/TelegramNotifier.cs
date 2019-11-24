using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Args;

namespace Exchange.Net
{
    public static class TelegramNotifier
    {
        static ITelegramBotClient botClient;
        public static List<Action<string>> ParseAndRun { get; }

        static TelegramNotifier()
        {
            ParseAndRun = new List<Action<string>>();
        }

        public static void Initialize()
        {
            //var httpProxy = new WebProxy("136.243.47.220", 3128);
            botClient = new TelegramBotClient("690037046:AAGnCVx8mpG0QexUi-tIcMyFJzjprlr_wJE"/*, httpProxy*/);
            var me = botClient.GetMeAsync().Result;
            botClient.OnMessage += Bot_OnMessage;
            botClient.StartReceiving();
        }

        private static void Bot_OnMessage(object sender, MessageEventArgs e)
        {
            Debug.Print($"Received a text message in chat {e.Message.Chat.Id}.");
            if (e.Message.Text.StartsWith(@"/"))
                ParseAndRun.ForEach(action => action(e.Message.Text));
        }

        public static async void Notify(string message)
        {
            int[] chats = { 542344060 };
            foreach (var id in chats)
            {
                var msg = await botClient.SendTextMessageAsync(id, message);
            }
        }
    }
}
