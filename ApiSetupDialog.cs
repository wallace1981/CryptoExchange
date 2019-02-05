using System;
using System.Security;
using Exchange.Net;
using System.IO;

namespace CryptoExchange
{
    public partial class ApiSetupDialog : Gtk.Dialog
    {
        public ApiSetupDialog()
        {
            this.Build();
        }

        protected void OnResponse(object o, Gtk.ResponseArgs args)
        {
            if (args.ResponseId == Gtk.ResponseType.Ok)
            {
                ApiKey = new SecureString();
                foreach (var ch in entryApiKey.Text)
                    ApiKey.AppendChar(ch);
                ApiSecret = new SecureString();
                foreach (var ch in entryApiSecret.Text)
                    ApiSecret.AppendChar(ch);
                ExchangeApiCore.SaveApiKeys(System.IO.Path.ChangeExtension(entryFileName.Text, ".hash").ToLower(), ApiKey, ApiSecret);
            }
        }

        public SecureString ApiKey { get; private set; }
        public SecureString ApiSecret { get; private set; }
    }
}
