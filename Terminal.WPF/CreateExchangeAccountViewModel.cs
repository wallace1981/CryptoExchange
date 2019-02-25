using Exchange.Net;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Security;
using System.Text;
using System.Threading.Tasks;

namespace Terminal.WPF
{
    public class CreateExchangeAccountViewModel : ReactiveObject, ISupportsActivation
    {
        public ViewModelActivator Activator { get; }
        public IEnumerable<ExchangeViewModel> Exchanges { get; }
        [Reactive] public ExchangeViewModel Exchange { get; set; }
        [Reactive] public string Name { get; set; }
        [Reactive] public SecureString ApiKey { get; set; }
        [Reactive] public SecureString ApiSecret { get; set; }

        public ReactiveCommand<Unit, Unit> Submit { get; private set; }
        public Interaction<Unit, bool> CloseDialog { get; }

        public CreateExchangeAccountViewModel(IEnumerable<ExchangeViewModel> exchangeList, ExchangeViewModel selectedExchange)
        {
            Activator = new ViewModelActivator();
            CloseDialog = new Interaction<Unit, bool>();

            Exchanges = exchangeList;
            Exchange = selectedExchange;

            this.WhenActivated(disposables =>
            {
                var canSubmit = this.WhenAnyValue(x => x.Name, x => x.ApiKey, x => x.ApiSecret,
                    (x, y, z) => !string.IsNullOrWhiteSpace(x) && y != null && z != null);
                Submit = ReactiveCommand.Create(SubmitImpl, canSubmit).DisposeWith(disposables);
            });
        }

        private async void SubmitImpl()
        {
            var path = $"{Exchange.ExchangeName}-{Name}.hash";
            ExchangeApiCore.SaveApiKeys(path, ApiKey, ApiSecret);
            var reg = Microsoft.Win32.Registry.CurrentUser.CreateSubKey($@"Software\wallace\Terminal.WPF\{Exchange.ExchangeName}\Accounts", writable: true);
            ExchangeApiCore.SaveApiKeys(reg, Name, ApiKey, ApiSecret);
            var ok = await CloseDialog.Handle(Unit.Default);
        }
    }
}
