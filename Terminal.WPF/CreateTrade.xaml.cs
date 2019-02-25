using Exchange.Net;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Linq;
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
    /// Interaction logic for CreateTrade.xaml
    /// </summary>
    public partial class CreateTrade : ReactiveUserControl<TradeTaskViewModel>
    {
        public CreateTrade()
        {
            InitializeComponent();
            this.WhenActivated(disposables =>
            {
                ViewModel
                    .Confirm
                    .RegisterHandler(
                        interaction =>
                        {
                            var result = MessageBox.Show(
                                Window.GetWindow(this),
                                interaction.Input,
                                "Caption", MessageBoxButton.OKCancel);

                            interaction.SetOutput(result == MessageBoxResult.OK);
                        }).DisposeWith(disposables);
                ViewModel.SubmitCommand.Subscribe(OnSubmitCommand).DisposeWith(disposables);
            });
        }

        private void OnSubmitCommand(bool x)
        {
            if (x)
            {
                var wnd = Window.GetWindow(this);
                wnd.DialogResult = true;
            }
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            //var wnd = Window.GetWindow(this);
            //wnd.DialogResult = true;
        }
    }
}
