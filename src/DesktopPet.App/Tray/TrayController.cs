using DesktopPet.App.Overlay;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace DesktopPet.App.Tray;

public sealed class TrayController : IDisposable
{
    private readonly PetOverlayWindow _overlayWindow;
    private readonly Action _showSettings;
    private readonly Action _showChat;
    private readonly Action _exitApplication;
    private readonly Forms.NotifyIcon _trayIcon;
    private readonly Forms.ToolStripMenuItem _clickThroughMenuItem;

    public TrayController(
        PetOverlayWindow overlayWindow,
        Action showSettings,
        Action showChat,
        Action exitApplication)
    {
        _overlayWindow = overlayWindow;
        _showSettings = showSettings;
        _showChat = showChat;
        _exitApplication = exitApplication;

        _clickThroughMenuItem = new Forms.ToolStripMenuItem("Click through")
        {
            CheckOnClick = true
        };

        _clickThroughMenuItem.CheckedChanged += OnClickThroughChanged;

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Show", null, (_, _) => ShowOverlay());
        menu.Items.Add("Hide", null, (_, _) => _overlayWindow.Hide());
        menu.Items.Add(_clickThroughMenuItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Settings", null, (_, _) => _showSettings());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => _exitApplication());

        _trayIcon = new Forms.NotifyIcon
        {
            ContextMenuStrip = menu,
            Icon = Drawing.SystemIcons.Application,
            Text = "Desktop Pet",
            Visible = true
        };

        _trayIcon.DoubleClick += (_, _) => ShowOverlay();
    }

    public void Dispose()
    {
        _clickThroughMenuItem.CheckedChanged -= OnClickThroughChanged;
        _trayIcon.Dispose();
    }

    private void OnClickThroughChanged(object? sender, EventArgs e)
    {
        _overlayWindow.SetClickThrough(_clickThroughMenuItem.Checked);
    }

    private void ShowOverlay()
    {
        _overlayWindow.ShowNearBottomRight();
    }
}
