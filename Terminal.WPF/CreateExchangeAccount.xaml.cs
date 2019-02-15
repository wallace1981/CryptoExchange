using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Terminal.WPF
{
    /// <summary>
    /// Interaction logic for CreateExchangeAccount.xaml
    /// </summary>
    public partial class CreateExchangeAccount : ReactiveUserControl<CreateExchangeAccountViewModel>
    {
        public CreateExchangeAccount()
        {
            InitializeComponent();
            this.WhenActivated(disposables => 
            {
                pwdApiKey.Events().PasswordChanged.Subscribe(x => ViewModel.ApiKey = pwdApiKey.SecurePassword).DisposeWith(disposables);
                pwdApiSecret.Events().PasswordChanged.Subscribe(x => ViewModel.ApiSecret = pwdApiSecret.SecurePassword).DisposeWith(disposables);
                ViewModel.CloseDialog.RegisterHandler(interaction => 
                {
                    var wnd = Window.GetWindow(this);
                    wnd.DialogResult = true;
                    interaction.SetOutput(wnd.DialogResult.GetValueOrDefault());
                });
            });
        }
    }
}
