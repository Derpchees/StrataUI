using System.Windows;
using System.Windows.Controls;
using Strata.Shell.Services;

namespace Strata.Shell.Controls;

/// <summary>Zona de la barra donde va un hijo de <see cref="BarZonesPanel"/>.</summary>
public enum BarPanelZone { Apps, Start, Center, End }

/// <summary>
/// Distribución de la barra a lo largo (horizontal o vertical): zona de inicio, zona central, zona
/// del final y el área de las ventanas, que reciben el espacio que sobra (su lista se desplaza si
/// no caben). Lo de la zona de las ventanas va dentro de su área; las demás zonas, en su lugar: lo
/// del centro queda exactamente al medio de la barra y las ventanas antes o después de él.
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

    public static readonly DependencyProperty AppsZoneProperty = DependencyProperty.Register(
        nameof(AppsZone), typeof(BarZone), typeof(BarZonesPanel),
        new FrameworkPropertyMetadata(BarZone.Start, FrameworkPropertyMetadataOptions.AffectsArrange));

    /// <summary>Zona de las ventanas: al principio, al centro o al final del espacio libre.</summary>
    public BarZone AppsZone
    {
        get => (BarZone)GetValue(AppsZoneProperty);
        set => SetValue(AppsZoneProperty, value);
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
        double from = startLen, to = Math.Max(from, total - endLen);

        Place(start, 0, startLen);
        Place(end, total - endLen, endLen);

        if (centerLen == 0)
        {
            // Las ventanas en el espacio entre las zonas (su alineación decide dónde; si van al
            // centro, lo del centro va dentro de su área).
            Place(apps, from, to - from);
            Place(center, from, 0);
        }
        else
        {
            // Centro al medio exacto de la barra; las ventanas antes o después de él.
            double at = Math.Clamp((total - centerLen) / 2, from, Math.Max(from, to - centerLen));
            if (AppsZone == BarZone.End)
                Place(apps, at + centerLen, Math.Max(0, to - at - centerLen));
            else
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
