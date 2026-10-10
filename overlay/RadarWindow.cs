using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace GameTracker;

/// <summary>
/// A round radar at the bottom left of the screen, north up: the player as an arrow (its heading) in the middle, the
/// spots of Radar around it (40 m to the edge; farther ones sit on the edge as rings), the nearest few with their distance
/// and a ▲/▼ when they are more than 3 m above or below. Shows while the overlay does (Allowed) and there is a spot;
/// clicks go through to the game and it never takes focus. Drawn by the overlay ten times a second.
/// </summary>
public sealed class RadarWindow : Window
{
    const double Size = 210, Radius = 96, RangeMetres = 40, Centre = Size / 2;
    readonly Canvas _canvas = new() { Width = Size, Height = Size };
    bool _hasContent, _allowed, _closed;

    static readonly Brush Back = Frozen("#D90A1220"), Edge = Frozen("#5EEAD4"), Ring = Frozen("#335EEAD4"), Me = Frozen("#F8FAFC"),
        Label = Frozen("#E2E8F0"), North = Frozen("#94A3B8");

    static Brush Colour(RadarKind kind) => kind switch
    {
        RadarKind.Quest => QuestBrush,
        RadarKind.Giver => GiverBrush,
        RadarKind.Story => StoryBrush,
        _ => ChestBrush,
    };
    static readonly Brush QuestBrush = Frozen("#34D399"), GiverBrush = Frozen("#A78BFA"), StoryBrush = Frozen("#38BDF8"), ChestBrush = Frozen("#FCD34D");

    public RadarWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowActivated = false;
        ShowInTaskbar = false;
        Width = Height = Size;
        FontFamily = new FontFamily("Bahnschrift");
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        UseLayoutRounding = true;
        Content = _canvas;
        SourceInitialized += (_, _) => new Native(HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)).SetClickThrough(true);
        Closed += (_, _) => _closed = true;
        var area = SystemParameters.WorkArea;
        Left = area.Left + 24;
        Top = area.Bottom - Size - 24;
    }

    /// <summary>False while the overlay is hidden (menus, battles, cutscenes, Ctrl+Shift+G).</summary>
    public bool Allowed
    {
        set { _allowed = value; Update(); }
    }

    /// <summary>Draws the spots around the player (null: not known, the radar hides); heading null draws a dot, not an arrow.</summary>
    public void Draw(GamePosition? me, double? heading, IReadOnlyList<RadarSpot> spots, IGameReader reader)
    {
        _hasContent = me is not null && spots.Count > 0;
        if (_hasContent && _allowed && !_closed)
        {
            _canvas.Children.Clear();
            DrawFrame();
            var placed = spots.Select(s => (s.Kind, Offset: Radar.Offset(reader, me!, s.At))).OrderByDescending(s => s.Offset.Flat).ToList();
            // Labels for the nearest three that are not chests (those show in the location panel with their distance).
            var labelled = placed.Where(s => s.Kind != RadarKind.Chest).OrderBy(s => s.Offset.Flat).Take(3).ToHashSet();
            foreach (var spot in placed) DrawSpot(spot.Kind, spot.Offset, labelled.Contains(spot));
            DrawMe(heading);
        }
        Update();
    }

    void DrawFrame()
    {
        _canvas.Children.Add(Circle(Radius * 2, Back, Edge, 1.5));
        _canvas.Children.Add(Circle(Radius, null, Ring, 1));
        var north = new TextBlock { Text = "N", Foreground = North, FontSize = 11, FontWeight = FontWeights.SemiBold };
        Canvas.SetLeft(north, Centre - 4);
        Canvas.SetTop(north, Centre - Radius + 2);
        _canvas.Children.Add(north);
    }

    void DrawSpot(RadarKind kind, RadarOffset at, bool label)
    {
        double flat = at.Flat, scale = Math.Min(flat, RangeMetres) / RangeMetres * (Radius - 8);
        double x = Centre + (flat < 0.01 ? 0 : at.East / flat * scale), y = Centre - (flat < 0.01 ? 0 : at.North / flat * scale);
        bool beyond = flat > RangeMetres;
        var brush = Colour(kind);
        var dot = beyond ? Circle(8, null, brush, 2) : Circle(kind == RadarKind.Chest ? 7 : 9, brush, Back, 1);
        Canvas.SetLeft(dot, x - dot.Width / 2);
        Canvas.SetTop(dot, y - dot.Height / 2);
        _canvas.Children.Add(dot);
        if (!label) return;
        string updown = at.Up > 3 ? " ▲" : at.Up < -3 ? " ▼" : "";
        var text = new TextBlock { Text = World.Metres(Math.Sqrt(flat * flat + at.Up * at.Up)) + updown, Foreground = Label, FontSize = 10.5 };
        Canvas.SetLeft(text, x + 6);
        Canvas.SetTop(text, y - 7);
        _canvas.Children.Add(text);
    }

    void DrawMe(double? heading)
    {
        if (heading is not { } degrees)
        {
            var dot = Circle(7, Me, null, 0);
            Canvas.SetLeft(dot, Centre - 3.5);
            Canvas.SetTop(dot, Centre - 3.5);
            _canvas.Children.Add(dot);
            return;
        }
        var arrow = new Polygon
        {
            Points = [new Point(Centre, Centre - 9), new Point(Centre + 6, Centre + 6), new Point(Centre, Centre + 2), new Point(Centre - 6, Centre + 6)],
            Fill = Me,
            RenderTransform = new RotateTransform(degrees, Centre, Centre),
        };
        _canvas.Children.Add(arrow);
    }

    static Ellipse Circle(double diameter, Brush? fill, Brush? stroke, double thickness)
    {
        var circle = new Ellipse { Width = diameter, Height = diameter, Fill = fill, Stroke = stroke, StrokeThickness = thickness };
        Canvas.SetLeft(circle, Centre - diameter / 2);
        Canvas.SetTop(circle, Centre - diameter / 2);
        return circle;
    }

    void Update()
    {
        if (_closed) return;
        bool show = _hasContent && _allowed;
        if (show && !IsVisible) Show();
        else if (!show && IsVisible) Hide();
    }

    static SolidColorBrush Frozen(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }
}
