using System;
using CryptoExchange;
using Gtk;

public partial class MainWindow : Gtk.Window
{
    public MainWindow() : base(Gtk.WindowType.Toplevel)
    {
        this.Build();
        Current = this;
        this.OverrideFont();
        botsview1.Initialize(binanceview1.viewModel);
        GLib.Timeout.Add(1000, UpdateStatus);

    }

    protected void OnDeleteEvent(object sender, DeleteEventArgs a)
    {
        Application.Quit();
        a.RetVal = true;
    }

    protected void OnAddActionActivated(object sender, EventArgs e)
    {
    }

    public static Gtk.Window Current { get; private set; }

    protected void OnToggleRefreshToggled(object sender, EventArgs e)
    {
    }

    protected void OnMediaPlayActionToggled(object sender, EventArgs e)
    {
        var view = notebook1.CurrentPageWidget as ExchangeView;
        if (view != null)
        {
            view.viewModel.Activate();
        }
    }

    protected void OnPropertiesActionActivated(object sender, EventArgs e)
    {
        var dlg = new ApiSetupDialog();
        dlg.ParentWindow = this.GdkWindow;
        dlg.Run();
        dlg.Destroy();
    }

    internal bool UpdateStatus()
    {
        var view = notebook1.CurrentPageWidget as ExchangeView;
        if (view != null)
        {
            var id = statusbar1.GetContextId(view.viewModel.ExchangeName);
            statusbar1.Pop(id);
            statusbar1.Push(id, view.viewModel.Status + string.Empty);
        }
        return true;
    }
}

[Gtk.TreeNode(ListOnly = true)]
public class MarketSummaryNode : Gtk.TreeNode
{

    public MarketSummaryNode(Binance.Market market)
    {
        Symbol = market.symbol;
        Status = market.status;
    }

    [Gtk.TreeNodeValue(Column = 0)]
	public string Symbol { get; }

	[Gtk.TreeNodeValue(Column = 1)]
	public string Status { get; }

	[Gtk.TreeNodeValue(Column = 2)]
	public string StatusColor
	{
		get
		{
			if (Status == "BREAK")
				return "red";
			else
				return "black";
		}
	}
}

public static class WidgetHelper
{
    const string MainFont = "Roboto";
    const string CondensedFont = "Roboto";

    private static void OverrideFont(this Widget widget, string fontFamily = MainFont, Pango.Weight weight = Pango.Weight.Normal)
    {
        if (fontFamily == null)
            fontFamily = MainFont;
        var font = Pango.FontDescription.FromString(fontFamily);
        if (fontFamily != null)
            font.Family = fontFamily;
        font.Weight = weight;
        widget?.ModifyFont(font);
    }

    private static void OverrideFont(this Notebook widget)
    {
        foreach (var tab in widget.Children)
        {
            var lbl = widget.GetTabLabel(tab);
            lbl.OverrideFont(MainFont, Pango.Weight.Semibold);
            (tab as Container)?.OverrideFont();
        }
    }

    private static void OverrideFont(this NodeView widget)
    {
        (widget as Container).OverrideFont(MainFont, Pango.Weight.Semibold);
        (widget as Widget).OverrideFont(CondensedFont);
    }

    public static void OverrideFont(this Container container, string fontFamily = MainFont, Pango.Weight fontWeight = Pango.Weight.Normal)
    {
        foreach (var child in container.AllChildren)
        {
            var ct = child.GetType();
            if (ct == typeof(Label) || ct == typeof(Entry) || ct == typeof(CheckButton))
            {
                (child as Widget).OverrideFont(fontFamily, fontWeight);
            }
            else if (ct == typeof(CellView))
            {
                (child as Widget).OverrideFont(fontFamily, fontWeight);
            }
            else if (child as Notebook != null)
            {
                (child as Notebook).OverrideFont();
            }
            else if (child as NodeView != null)
            {
                (child as NodeView).OverrideFont();
            }
            else if (child as Container != null)
            {
                (child as Container).OverrideFont(fontFamily, fontWeight);
            }
        }
    }
}

public static class StringFormatHelper
{
    public static string TimestampToString(this DateTime timestamp)
    {
        return timestamp.ToString("dd-MM HH:mm:ss");
    }
}
