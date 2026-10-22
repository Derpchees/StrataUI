using System.ComponentModel;
using Strata.Shell.Services.Localization;

namespace Strata.Shell.Services.Notifications;

/// <summary>
/// Las notificaciones de una misma app (o sitio, o app web) en el centro de notificaciones, como en
/// el celular: una tarjeta con la más reciente; desplegada, las demás debajo en la misma tarjeta.
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
    /// <summary>Las demás (se ven al desplegar).</summary>
    public IEnumerable<StrataNotification> Others => Items.Skip(1);
    public int Count => Items.Count;
    public bool IsGroup => Count > 1;
    public string DismissText => IsGroup ? Loc.T("Quitar todas") : Loc.T("Quitar");

    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value)
                return;
            _isExpanded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
        }
    }

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
