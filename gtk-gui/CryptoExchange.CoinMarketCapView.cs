
// This file has been generated by the GUI designer. Do not modify.
namespace CryptoExchange
{
	public partial class CoinMarketCapView
	{
		private global::Gtk.VBox vbox1;

		private global::Gtk.HBox hbox2;

		private global::Gtk.Button button1;

		private global::Gtk.ComboBox combobox1;

		private global::Gtk.Label label2;

		private global::Gtk.ScrolledWindow GtkScrolledWindow;

		private global::Gtk.NodeView nodeview1;

		protected virtual void Build()
		{
			global::Stetic.Gui.Initialize(this);
			// Widget CryptoExchange.CoinMarketCapView
			global::Stetic.BinContainer.Attach(this);
			this.Name = "CryptoExchange.CoinMarketCapView";
			// Container child CryptoExchange.CoinMarketCapView.Gtk.Container+ContainerChild
			this.vbox1 = new global::Gtk.VBox();
			this.vbox1.Name = "vbox1";
			this.vbox1.Spacing = 6;
			// Container child vbox1.Gtk.Box+BoxChild
			this.hbox2 = new global::Gtk.HBox();
			this.hbox2.Name = "hbox2";
			this.hbox2.Spacing = 6;
			// Container child hbox2.Gtk.Box+BoxChild
			this.button1 = new global::Gtk.Button();
			this.button1.CanFocus = true;
			this.button1.Name = "button1";
			this.button1.UseUnderline = true;
			this.button1.Label = global::Mono.Unix.Catalog.GetString("Refresh");
			this.hbox2.Add(this.button1);
			global::Gtk.Box.BoxChild w1 = ((global::Gtk.Box.BoxChild)(this.hbox2[this.button1]));
			w1.Position = 0;
			w1.Expand = false;
			w1.Fill = false;
			// Container child hbox2.Gtk.Box+BoxChild
			this.combobox1 = global::Gtk.ComboBox.NewText();
			this.combobox1.AppendText(global::Mono.Unix.Catalog.GetString("1 day"));
			this.combobox1.AppendText(global::Mono.Unix.Catalog.GetString("3 days"));
			this.combobox1.AppendText(global::Mono.Unix.Catalog.GetString("1 week"));
			this.combobox1.AppendText(global::Mono.Unix.Catalog.GetString("2 weeks"));
			this.combobox1.AppendText(global::Mono.Unix.Catalog.GetString("1 month"));
			this.combobox1.Name = "combobox1";
			this.combobox1.Active = 0;
			this.hbox2.Add(this.combobox1);
			global::Gtk.Box.BoxChild w2 = ((global::Gtk.Box.BoxChild)(this.hbox2[this.combobox1]));
			w2.PackType = ((global::Gtk.PackType)(1));
			w2.Position = 1;
			w2.Expand = false;
			w2.Fill = false;
			// Container child hbox2.Gtk.Box+BoxChild
			this.label2 = new global::Gtk.Label();
			this.label2.Name = "label2";
			this.label2.LabelProp = global::Mono.Unix.Catalog.GetString("Compare with last");
			this.hbox2.Add(this.label2);
			global::Gtk.Box.BoxChild w3 = ((global::Gtk.Box.BoxChild)(this.hbox2[this.label2]));
			w3.PackType = ((global::Gtk.PackType)(1));
			w3.Position = 2;
			w3.Expand = false;
			w3.Fill = false;
			this.vbox1.Add(this.hbox2);
			global::Gtk.Box.BoxChild w4 = ((global::Gtk.Box.BoxChild)(this.vbox1[this.hbox2]));
			w4.Position = 0;
			w4.Expand = false;
			w4.Fill = false;
			// Container child vbox1.Gtk.Box+BoxChild
			this.GtkScrolledWindow = new global::Gtk.ScrolledWindow();
			this.GtkScrolledWindow.Name = "GtkScrolledWindow";
			this.GtkScrolledWindow.ShadowType = ((global::Gtk.ShadowType)(1));
			// Container child GtkScrolledWindow.Gtk.Container+ContainerChild
			this.nodeview1 = new global::Gtk.NodeView();
			this.nodeview1.CanFocus = true;
			this.nodeview1.Name = "nodeview1";
			this.GtkScrolledWindow.Add(this.nodeview1);
			this.vbox1.Add(this.GtkScrolledWindow);
			global::Gtk.Box.BoxChild w6 = ((global::Gtk.Box.BoxChild)(this.vbox1[this.GtkScrolledWindow]));
			w6.Position = 1;
			this.Add(this.vbox1);
			if ((this.Child != null))
			{
				this.Child.ShowAll();
			}
			this.Hide();
			this.button1.Clicked += new global::System.EventHandler(this.OnButton1Clicked);
		}
	}
}
