using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace TrailStumbler.Services;

/// <summary>
/// Remembers the Windows app window's position, size and maximized state between runs (Preferences), and keeps it
/// within the work area of a display that's there: never under the taskbar, never on a monitor since unplugged.
/// </summary>
public static class WindowPlacement
{
    private const string Key = "window_placement";   // "x,y,width,height,maximized", physical pixels

    public static void Attach(Microsoft.UI.Xaml.Window window)
    {
        var app = window.AppWindow;
        var saved = Load();
        var rect = saved?.Rect ?? new RectInt32(app.Position.X, app.Position.Y, app.Size.Width, app.Size.Height);
        app.MoveAndResize(FitToWorkArea(rect));
        if (saved?.Maximized == true && app.Presenter is OverlappedPresenter presenter) presenter.Maximize();

        app.Changed += (_, e) =>
        {
            if (e.DidPositionChange || e.DidSizeChange || e.DidPresenterChange) Save(app);
        };
    }

    /// <summary>The rectangle moved and shrunk as needed to lie within the nearest display's work area.</summary>
    internal static RectInt32 FitToWorkArea(RectInt32 r)
    {
        var area = DisplayArea.GetFromRect(r, DisplayAreaFallback.Nearest).WorkArea;
        int w = Math.Clamp(r.Width, 400, area.Width), h = Math.Clamp(r.Height, 400, area.Height);
        int x = Math.Clamp(r.X, area.X, area.X + area.Width - w);
        int y = Math.Clamp(r.Y, area.Y, area.Y + area.Height - h);
        return new RectInt32(x, y, w, h);
    }

    private static void Save(AppWindow app)
    {
        if (app.Presenter is not OverlappedPresenter p || p.State == OverlappedPresenterState.Minimized) return;
        bool maximized = p.State == OverlappedPresenterState.Maximized;
        // Maximized, the size to come back to is the last one before
        var keep = maximized ? Load()?.Rect : new RectInt32(app.Position.X, app.Position.Y, app.Size.Width, app.Size.Height);
        if (keep is not { } r) return;
        try { Preferences.Default.Set(Key, $"{r.X},{r.Y},{r.Width},{r.Height},{(maximized ? 1 : 0)}"); } catch { }
    }

    private static (RectInt32 Rect, bool Maximized)? Load()
    {
        try
        {
            var parts = Preferences.Default.Get(Key, "").Split(',');
            if (parts.Length != 5 || !parts.All(p => int.TryParse(p, out _))) return null;
            var n = parts.Select(int.Parse).ToArray();
            if (n[2] <= 0 || n[3] <= 0) return null;
            return (new RectInt32(n[0], n[1], n[2], n[3]), n[4] == 1);
        }
        catch { return null; }
    }
}
