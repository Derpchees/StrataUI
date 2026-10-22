using System.IO;
using Strata.Shell.Services.Notifications;
using Xunit;

namespace Strata.Tests;

/// <summary>Notificaciones agrupadas por app y activadores de las apps empaquetadas.</summary>
public class NotificationGroupTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    private static StrataNotification Toast(uint id, int minutesAgo, string app, string? appId = null, string? site = null) => new()
    {
        Id = id,
        AppName = app,
        AppId = appId,
        SiteDomain = site,
        Time = Now.AddMinutes(-minutesAgo),
    };

    [Fact]
    public void Notifications_of_the_same_app_go_together_newest_group_first()
    {
        var items = new[]
        {
            Toast(1, 1, "WhatsApp", "5319275A.WhatsAppDesktop_cv1g1gvanyjgm!App"),
            Toast(2, 2, "Outlook", "Microsoft.OutlookForWindows_8wekyb3d8bbwe!Microsoft.OutlookforWindows"),
            Toast(3, 5, "WhatsApp", "5319275A.WhatsAppDesktop_cv1g1gvanyjgm!App"),
            Toast(4, 9, "WhatsApp", "5319275A.WhatsAppDesktop_cv1g1gvanyjgm!App"),
        };
        var groups = NotificationGroup.Build(items, new HashSet<string>());
        Assert.Equal(["WhatsApp", "Outlook"], groups.Select(g => g.Latest.AppName));
        Assert.Equal([1u, 3u, 4u], groups[0].Items.Select(n => n.Id));
        Assert.Equal([3u, 4u], groups[0].Others.Select(n => n.Id));
        Assert.False(groups[0].IsExpanded);
        Assert.False(groups[1].IsGroup);
        Assert.Empty(groups[1].Others);
    }

    [Fact]
    public void Sites_group_by_domain_even_from_different_browsers()
    {
        var items = new[]
        {
            Toast(1, 1, "WhatsApp", "Brave", "web.whatsapp.com"),
            Toast(2, 2, "WhatsApp", "Chrome", "web.whatsapp.com"),
            Toast(3, 3, "Gmail", "Brave", "mail.google.com"),
        };
        var groups = NotificationGroup.Build(items, new HashSet<string>());
        Assert.Equal(2, groups.Count);
        Assert.Equal(2, groups[0].Count);
    }

    [Fact]
    public void Apps_without_identity_group_by_name_and_expanded_groups_stay_open()
    {
        var items = new[] { Toast(1, 1, "Tailscale"), Toast(2, 2, "Tailscale"), Toast(3, 3, "Otra") };
        var key = NotificationGroup.KeyOf(items[0]);
        var groups = NotificationGroup.Build(items, new HashSet<string> { key });
        Assert.Equal(2, groups.Count);
        Assert.True(groups[0].IsExpanded);
        Assert.False(groups[1].IsExpanded);
    }

    [Fact]
    public void Packaged_activator_is_read_only_from_an_exe_server()
    {
        string manifest = Path.Combine(Path.GetTempPath(), $"strata-manifest-{Guid.NewGuid():N}.xml");
        File.WriteAllText(manifest, """
            <?xml version="1.0" encoding="utf-8"?>
            <Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
                     xmlns:desktop="http://schemas.microsoft.com/appx/manifest/desktop/windows10"
                     xmlns:com="http://schemas.microsoft.com/appx/manifest/com/windows10">
              <Applications>
                <Application Id="App">
                  <Extensions>
                    <desktop:Extension Category="windows.toastNotificationActivation">
                      <desktop:ToastNotificationActivation ToastActivatorCLSID="bebd6a5e-1b14-4a57-a195-c772d28b6d15" />
                    </desktop:Extension>
                    <com:Extension Category="windows.comServer">
                      <com:ComServer>
                        <com:ExeServer Executable="WhatsApp.Root.exe" Arguments="----AppNotificationActivated:">
                          <com:Class Id="bebd6a5e-1b14-4a57-a195-c772d28b6d15" />
                        </com:ExeServer>
                      </com:ComServer>
                    </com:Extension>
                  </Extensions>
                </Application>
                <Application Id="Dll">
                  <Extensions>
                    <desktop:Extension Category="windows.toastNotificationActivation">
                      <desktop:ToastNotificationActivation ToastActivatorCLSID="916f9b5d-b5b2-4d36-b047-03c7a52f81c8" />
                    </desktop:Extension>
                    <com:Extension Category="windows.comServer">
                      <com:ComServer>
                        <com:SurrogateServer>
                          <com:Class Id="916f9b5d-b5b2-4d36-b047-03c7a52f81c8" Path="notificationserver.dll" ThreadingModel="Both" />
                        </com:SurrogateServer>
                      </com:ComServer>
                    </com:Extension>
                  </Extensions>
                </Application>
              </Applications>
            </Package>
            """);
        try
        {
            Assert.Equal(new Guid("bebd6a5e-1b14-4a57-a195-c772d28b6d15"), ToastActivator.FromManifest(manifest, "App"));
            Assert.Null(ToastActivator.FromManifest(manifest, "Dll"));
        }
        finally
        {
            File.Delete(manifest);
        }
    }
}
