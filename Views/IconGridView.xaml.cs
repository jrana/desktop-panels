using DesktopPanels.Models;
using DesktopPanels.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace DesktopPanels.Views;

public sealed partial class IconGridView : UserControl
{
    public string PanelId { get; set; } = "";
    public IntPtr OwnerHwnd { get; set; }
    public bool AllowDrag { get; set; }
    public bool AllowRemove { get; set; }
    public string EmptyMessage { get; set; } = "Nothing to show";

    public IconGridView()
    {
        InitializeComponent();
        AllowDrop = true;
        DragOver += OnDragOver;
        Drop += OnDrop;
    }

    public void SetTopInset()
    {
        Tiles.Margin = new Thickness(10, 34, 10, 12);
    }

    public void SetItems(IReadOnlyList<DesktopIcon> items)
    {
        Tiles.Children.Clear();
        EmptyText.Text = EmptyMessage;
        EmptyText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var item in items)
        {
            Tiles.Children.Add(CreateTile(item));
        }
    }

    UIElement CreateTile(DesktopIcon item)
    {
        var image = new Image
        {
            Width = 40,
            Height = 40,
            Source = item.Icon,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var label = new TextBlock
        {
            Text = item.DisplayName,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            MaxLines = 2,
            FontSize = 12,
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(2, 6, 2, 0)
        };
        var stack = new StackPanel
        {
            Width = 88,
            Padding = new Thickness(2),
            CanDrag = AllowDrag,
            Tag = item
        };
        stack.Children.Add(image);
        stack.Children.Add(label);
        stack.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(stack).Properties.IsRightButtonPressed)
            {
                return;
            }

            var point = e.GetCurrentPoint(stack);
            if (point.Properties.PointerUpdateKind == Microsoft.UI.Input.PointerUpdateKind.LeftButtonPressed
                && e.Pointer.PointerDeviceType != Microsoft.UI.Input.PointerDeviceType.Mouse)
            {
                return;
            }
        };
        stack.DoubleTapped += (_, _) =>
        {
            try
            {
                ShellDesktop.Open(item.ParsingName);
            }
            catch
            {
                // The shell reports its own errors for broken shortcuts.
            }
        };
        stack.RightTapped += async (_, e) =>
        {
            e.Handled = true;
            if (ShellContextMenu.TryShow(OwnerHwnd, item.ParsingName, AllowRemove, PanelId, out var rename))
            {
                if (rename)
                {
                    await RenameItemAsync(item.ParsingName);
                }

                return;
            }

            ShowBasicMenu(item, stack, e.GetPosition(stack));
        };
        stack.DragStarting += (_, e) =>
        {
            e.Data.SetText(item.ParsingName);
            e.Data.RequestedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
            e.Data.Properties.Add("SourcePanelId", PanelId);
            DragPayload.SourcePanelId = PanelId;
            DragPayload.ParsingName = item.ParsingName;
        };
        return stack;
    }

    void OnDragOver(object sender, DragEventArgs e)
    {
        if (!AllowDrag)
        {
            e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.None;
            return;
        }

        e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move;
        e.Handled = true;
    }

    async void OnDrop(object sender, DragEventArgs e)
    {
        if (!AllowDrag)
        {
            return;
        }

        var parsingName = DragPayload.ParsingName;
        var source = DragPayload.SourcePanelId;
        if (string.IsNullOrEmpty(parsingName) && e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Text))
        {
            parsingName = await e.DataView.GetTextAsync();
        }

        if (!string.IsNullOrEmpty(parsingName) && !string.IsNullOrEmpty(source))
        {
            PanelManager.MoveIcon(source, PanelId, parsingName);
        }

        DragPayload.Clear();
        e.Handled = true;
    }

    void ShowBasicMenu(DesktopIcon item, UIElement anchor, Windows.Foundation.Point position)
    {
        var menu = new MenuFlyout();
        var open = new MenuFlyoutItem { Text = "Open" };
        open.Click += (_, _) =>
        {
            try
            {
                ShellDesktop.Open(item.ParsingName);
            }
            catch
            {
                // Ignore a single failed launch.
            }
        };
        menu.Items.Add(open);

        if (ShellContextMenu.CanRename(item.ParsingName))
        {
            var rename = new MenuFlyoutItem { Text = "Rename" };
            rename.Click += async (_, _) => await RenameItemAsync(item.ParsingName);
            menu.Items.Add(rename);

            var delete = new MenuFlyoutItem { Text = "Delete" };
            delete.Click += (_, _) =>
            {
                ShellContextMenu.Recycle(OwnerHwnd, item.ParsingName);
                PanelManager.RefreshAfterItemChange();
            };
            menu.Items.Add(delete);
        }

        var properties = new MenuFlyoutItem { Text = "Properties" };
        properties.Click += (_, _) => ShellContextMenu.ShowProperties(item.ParsingName);
        menu.Items.Add(properties);

        if (AllowRemove)
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            var remove = new MenuFlyoutItem { Text = "Remove from this panel" };
            remove.Click += (_, _) => PanelManager.ReturnToDefault(PanelId, item.ParsingName);
            menu.Items.Add(remove);
        }

        menu.ShowAt(anchor, position);
    }

    async Task RenameItemAsync(string parsingName)
    {
        if (!ShellContextMenu.CanRename(parsingName) || XamlRoot is null)
        {
            return;
        }

        var extension = Path.GetExtension(parsingName);
        var original = Path.GetFileName(parsingName);
        var hideExtension = extension.Length > 0 && ExtensionsHidden();
        var shown = hideExtension ? Path.GetFileNameWithoutExtension(parsingName) : original;
        var box = new TextBox
        {
            Text = shown,
            Width = 320
        };
        var dialog = new ContentDialog
        {
            Title = "Rename",
            Content = box,
            PrimaryButtonText = "Rename",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };
        dialog.Opened += (_, _) =>
        {
            box.Focus(FocusState.Programmatic);
            box.SelectAll();
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (!TryFinalName(box.Text, original, hideExtension, extension, out var finalName, out var unchanged)
                || (!unchanged && NameTaken(parsingName, finalName)))
            {
                args.Cancel = true;
            }
        };

        NativeWindow.SuspendZOrder = true;
        NativeWindow.BringToFront(OwnerHwnd);
        ContentDialogResult result;
        try
        {
            result = await dialog.ShowAsync();
        }
        finally
        {
            NativeWindow.SuspendZOrder = false;
        }

        if (result != ContentDialogResult.Primary
            || !TryFinalName(box.Text, original, hideExtension, extension, out var name, out var sameName)
            || sameName)
        {
            return;
        }

        var parent = Path.GetDirectoryName(parsingName);
        if (string.IsNullOrEmpty(parent) || NameTaken(parsingName, name))
        {
            return;
        }

        var destination = Path.Combine(parent, name);
        try
        {
            if (Directory.Exists(parsingName))
            {
                Directory.Move(parsingName, destination);
            }
            else
            {
                File.Move(parsingName, destination);
            }
        }
        catch
        {
            return;
        }

        PanelManager.RefreshAfterItemChange();
    }

    static bool TryFinalName(string typed, string original, bool hideExtension, string extension, out string name, out bool unchanged)
    {
        name = typed.Trim();
        unchanged = false;
        if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return false;
        }

        if (hideExtension && !name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
        {
            name += extension;
        }

        unchanged = name.Equals(original, StringComparison.OrdinalIgnoreCase);
        return true;
    }

    static bool NameTaken(string parsingName, string finalName)
    {
        var parent = Path.GetDirectoryName(parsingName);
        if (string.IsNullOrEmpty(parent))
        {
            return true;
        }

        var destination = Path.Combine(parent, finalName);
        if (destination.Equals(parsingName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return File.Exists(destination) || Directory.Exists(destination);
    }

    static bool ExtensionsHidden()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced");
            return key?.GetValue("HideFileExt") is int value && value != 0;
        }
        catch
        {
            return true;
        }
    }
}

static class DragPayload
{
    public static string? SourcePanelId { get; set; }
    public static string? ParsingName { get; set; }
    public static void Clear()
    {
        SourcePanelId = null;
        ParsingName = null;
    }
}
