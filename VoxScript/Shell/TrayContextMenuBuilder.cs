// VoxScript/Shell/TrayContextMenuBuilder.cs
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace VoxScript.Shell;

/// <summary>
/// Builds the context menu shown when right-clicking the system tray icon.
/// </summary>
public static class TrayContextMenuBuilder
{
    public static MenuFlyout Build(Window mainWindow, Action onQuit)
    {
        var menu = new MenuFlyout();

        var showItem = new MenuFlyoutItem { Text = "Show VoxScript" };
        showItem.Click += (_, _) => mainWindow.Activate();
        menu.Items.Add(showItem);

        menu.Items.Add(new MenuFlyoutSeparator());

        var quitItem = new MenuFlyoutItem { Text = "Quit" };
        quitItem.Click += (_, _) => onQuit();
        menu.Items.Add(quitItem);

        return menu;
    }
}
