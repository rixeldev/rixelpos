using Avalonia;
using Avalonia.Controls;

namespace RixelPOS.Desktop.Services;

/// <summary>
/// Keeps windows inside the usable screen area on any DPI. Avalonia works in
/// logical pixels, while Screen.WorkingArea is reported in physical pixels, so
/// the area is divided by the monitor scaling before clamping.
/// </summary>
public static class WindowSizing
{
    private const double ScreenMargin = 24;

    public static void ClampOnOpened(Window window)
    {
        window.Opened += (_, _) => Clamp(window);
    }

    public static void Clamp(Window window)
    {
        try
        {
            var screen = window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary;
            if (screen is null)
            {
                return;
            }

            var scale = screen.Scaling <= 0 ? 1.0 : screen.Scaling;
            var availableWidth = Math.Max(420, screen.WorkingArea.Width / scale - ScreenMargin);
            var availableHeight = Math.Max(360, screen.WorkingArea.Height / scale - ScreenMargin);

            if (window.MinWidth > availableWidth)
            {
                window.MinWidth = availableWidth;
            }

            if (window.MinHeight > availableHeight)
            {
                window.MinHeight = availableHeight;
            }

            if (!double.IsNaN(window.Width) && window.Width > availableWidth)
            {
                window.Width = availableWidth;
            }

            if (!double.IsNaN(window.Height) && window.Height > availableHeight)
            {
                window.Height = availableHeight;
            }

            if (window.SizeToContent != SizeToContent.Manual)
            {
                window.MaxWidth = availableWidth;
                window.MaxHeight = availableHeight;
            }

            // Reposition after resizing so the window stays visually centered on
            // its monitor instead of growing off-screen.
            if (window.WindowState == WindowState.Normal)
            {
                var widthPx = (int)Math.Round(window.Bounds.Width * scale);
                var heightPx = (int)Math.Round(window.Bounds.Height * scale);
                if (window.WindowStartupLocation == WindowStartupLocation.CenterOwner && window.Owner is Window owner)
                {
                    var ownerWidth = (int)Math.Round(owner.Bounds.Width * scale);
                    var ownerHeight = (int)Math.Round(owner.Bounds.Height * scale);
                    window.Position = new Avalonia.PixelPoint(
                        owner.Position.X + Math.Max(0, (ownerWidth - widthPx) / 2),
                        owner.Position.Y + Math.Max(0, (ownerHeight - heightPx) / 2));
                }
                else
                {
                    window.Position = new Avalonia.PixelPoint(
                        screen.WorkingArea.X + Math.Max(0, (screen.WorkingArea.Width - widthPx) / 2),
                        screen.WorkingArea.Y + Math.Max(0, (screen.WorkingArea.Height - heightPx) / 2));
                }
            }
        }
        catch
        {
            // Never prevent a window from opening because screens could not be queried.
        }
    }
}