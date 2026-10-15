using System.Windows;
using System.Windows.Controls;

namespace Strata.Shell.Controls;

/// <summary>Zona de la barra donde va un hijo de <see cref="BarZonesPanel"/>.</summary>
public enum BarPanelZone { Apps, Start, Center, End }

/// <summary>
/// Distribución de la barra a lo largo (horizontal o vertical): zona de inicio, Inicio con las
/// ventanas, zona central y zona del final. Lo del centro queda exactamente al medio de la barra
/// si las ventanas van al principio; si van centradas, se agrupa con ellas (justo después) para no
/// encimarse. Las ventanas reciben el espacio que sobra (su lista se desplaza si no caben).
/// </summary>
public sealed class BarZonesPanel : Panel
{
    public static readonly DependencyProperty ZoneProperty = DependencyProperty.RegisterAttached(
        "Zone", typeof(BarPanelZone), typeof(BarZonesPanel),
        new FrameworkPropertyMetadata(BarPanelZone.Apps, FrameworkPropertyMetadataOptions.AffectsParentArrange | FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    public static BarPanelZone GetZone(UIElement element) => (BarPanelZone)element.GetValue(ZoneProperty);
    public static void SetZone(UIElement element, BarPanelZone zone) => element.SetValue(ZoneProperty, zone);

    public static readonly DependencyProperty OrientationProperty = DependencyProperty.Register(
        nameof(Orientation), typeof(Orientation), typeof(BarZonesPanel),
        new FrameworkPropertyMetadata(Orientation.Horizontal, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    public static readonly DependencyProperty AppsCenteredProperty = DependencyProperty.Register(
        nameof(AppsCentered), typeof(bool), typeof(BarZonesPanel),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsArrange));

    /// <summary>Las ventanas van centradas (y lo del centro se agrupa con ellas).</summary>
    public bool AppsCentered
    {
        get => (bool)GetValue(AppsCenteredProperty);
        set => SetValue(AppsCenteredProperty, value);
    }

    private bool Horizontal => Orientation == Orientation.Horizontal;

    private double Along(Size size) => Horizontal ? size.Width : size.Height;
    private double Across(Size size) => Horizontal ? size.Height : size.Width;
    private Size Make(double along, double across) => Horizontal ? new Size(along, across) : new Size(across, along);

    private UIElement? Child(BarPanelZone zone) =>
        InternalChildren.Cast<UIElement>().FirstOrDefault(c => GetZone(c) == zone);

    private static double Visible(UIElement? element, Func<Size, double> along) =>
        element is { Visibility: not Visibility.Collapsed } ? along(element.DesiredSize) : 0;

    protected override Size MeasureOverride(Size available)
    {
        double total = Along(available), across = Across(available);
        double used = 0, thickness = 0;
        foreach (var zone in new[] { BarPanelZone.Start, BarPanelZone.Center, BarPanelZone.End })
        {
            if (Child(zone) is not { } child)
                continue;
            child.Measure(Make(double.PositiveInfinity, across));
            used += Visible(child, Along);
            thickness = Math.Max(thickness, Visible(child, Across));
        }
        if (Child(BarPanelZone.Apps) is { } apps)
        {
            apps.Measure(Make(Math.Max(0, double.IsInfinity(total) ? double.PositiveInfinity : total - used), across));
            used += Visible(apps, Along);
            thickness = Math.Max(thickness, Visible(apps, Across));
        }
        return Make(double.IsInfinity(total) ? used : total, double.IsInfinity(across) ? thickness : across);
    }

    protected override Size ArrangeOverride(Size final)
    {
        double total = Along(final), across = Across(final);
        var start = Child(BarPanelZone.Start);
        var center = Child(BarPanelZone.Center);
        var end = Child(BarPanelZone.End);
        var apps = Child(BarPanelZone.Apps);
        double startLen = Visible(start, Along), centerLen = Visible(center, Along), endLen = Visible(end, Along);
        double appsLen = Visible(apps, Along);
        double from = startLen, to = Math.Max(from, total - endLen);

        Place(start, 0, startLen);
        Place(end, total - endLen, endLen);

        if (centerLen == 0)
        {
            // Como siempre: las ventanas en el espacio entre las zonas (su alineación decide dónde).
            Place(apps, from, to - from);
            Place(center, from, 0);
        }
        else if (AppsCentered)
        {
            // Ventanas y centro juntos, centrados en la barra (sin salirse del espacio libre).
            double group = Math.Min(appsLen + centerLen, to - from);
            double at = Math.Clamp((total - group) / 2, from, Math.Max(from, to - group));
            double appsRoom = Math.Max(0, group - centerLen);
            Place(apps, at, appsRoom);
            Place(center, at + appsRoom, centerLen);
        }
        else
        {
            // Centro al medio exacto de la barra; las ventanas antes de él.
            double at = Math.Clamp((total - centerLen) / 2, from, Math.Max(from, to - centerLen));
            Place(apps, from, Math.Max(0, at - from));
            Place(center, at, centerLen);
        }
        return final;

        void Place(UIElement? element, double offset, double length)
        {
            if (element is null)
                return;
            var rect = Horizontal ? new Rect(offset, 0, Math.Max(0, length), across) : new Rect(0, offset, across, Math.Max(0, length));
            element.Arrange(rect);
        }
    }
}
