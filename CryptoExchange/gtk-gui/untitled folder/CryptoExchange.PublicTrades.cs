
// This file has been generated by the GUI designer. Do not modify.
namespace CryptoExchange
{
	public partial class PublicTrades
	{
		private global::Gtk.ScrolledWindow GtkScrolledWindow;

		private global::Gtk.NodeView nodeview1;

		protected virtual void Build()
		{
			global::Stetic.Gui.Initialize(this);
			// Widget CryptoExchange.PublicTrades
			global::Stetic.BinContainer.Attach(this);
			this.Name = "CryptoExchange.PublicTrades";
			// Container child CryptoExchange.PublicTrades.Gtk.Container+ContainerChild
			this.GtkScrolledWindow = new global::Gtk.ScrolledWindow();
			this.GtkScrolledWindow.Name = "GtkScrolledWindow";
			this.GtkScrolledWindow.ShadowType = ((global::Gtk.ShadowType)(1));
			// Container child GtkScrolledWindow.Gtk.Container+ContainerChild
			this.nodeview1 = new global::Gtk.NodeView();
			this.nodeview1.CanFocus = true;
			this.nodeview1.Name = "nodeview1";
			this.GtkScrolledWindow.Add(this.nodeview1);
			this.Add(this.GtkScrolledWindow);
			if ((this.Child != null))
			{
				this.Child.ShowAll();
			}
			this.Hide();
			this.nodeview1.Shown += new global::System.EventHandler(this.OnNodeview1Shown);
		}
	}
}
