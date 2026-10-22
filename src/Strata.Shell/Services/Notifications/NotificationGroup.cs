using System.ComponentModel;
using Strata.Shell.Services.Localization;

namespace Strata.Shell.Services.Notifications;

/// <summary>
/// Las notificaciones de una misma app (o sitio, o app web) en el centro de notificaciones, como en
/// el celular: plegadas muestran la más reciente con las demás apiladas detrás; desplegadas, todas.
/// </summary>
public sealed class NotificationGroup : INotifyPropertyChanged
{
    public NotificationGroup(string key, IReadOnlyList<StrataNotification> items, bool expanded)
    {
        Key = key;
        Items = items;
        _isExpanded = expanded;
    }

    public string Key { get; }
    /// <summary>De la más reciente a la más vieja.</summary>
    public IReadOnlyList<StrataNotification> Items { get; }
    public StrataNotification Latest => Items[0];
    public int Count => Items.Count;
    public bool IsGroup => Count > 1;
    /// <summary>Hay al menos tres: se ven dos capas detrás de la primera.</summary>
    public bool HasSecondLayer => Count > 2;
    public string CountText => Loc.F("{0} notificaciones", Count);

    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value)
                return;
            _isExpanded = value;
            foreach (var name in new[] { nameof(IsExpanded), nameof(IsStacked), nameof(ShowsAll) })
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    /// <summary>Plegado con varias: la primera y la pila.</summary>
    public bool IsStacked => IsGroup && !_isExpanded;
    /// <summary>Desplegado (o una sola): todas en la lista.</summary>
    public bool ShowsAll => !IsStacked;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// De qué grupo es: el sitio (WhatsApp Web y WhatsApp instalado como app web caen juntos si su
    /// dominio coincide), si no la identidad de la app y, sin ella, su nombre.
    /// </summary>
    public static string KeyOf(StrataNotification item) =>
        item.SiteDomain is { Length: > 0 } site ? "site:" + site.ToLowerInvariant()
        : item.AppId is { Length: > 0 } id ? "app:" + id.ToLowerInvariant()
        : "name:" + item.AppName.ToLowerInvariant();

    /// <summary>
    /// Agrupa las notificaciones (que llegan de la más reciente a la más vieja); los grupos quedan
    /// ordenados por su notificación más reciente. <paramref name="expanded"/> son las claves desplegadas.
    /// </summary>
    public static List<NotificationGroup> Build(IEnumerable<StrataNotification> items, ISet<string> expanded) =>
        items.GroupBy(KeyOf)
             .Select(g => new NotificationGroup(g.Key, g.OrderByDescending(n => n.Time).ToList(), expanded.Contains(g.Key)))
             .OrderByDescending(g => g.Latest.Time)
             .ToList();
}
