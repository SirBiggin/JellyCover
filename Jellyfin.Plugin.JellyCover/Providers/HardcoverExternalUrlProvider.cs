using System.Collections.Generic;
using System.Globalization;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.JellyCover.Providers;

/// <summary>
/// Adds "Hardcover" links to books and authors that carry a Hardcover id.
/// Uses the stable /id/ redirect so renamed slugs keep working.
/// </summary>
public class HardcoverExternalUrlProvider : IExternalUrlProvider
{
    /// <inheritdoc />
    public string Name => Plugin.ProviderName;

    /// <inheritdoc />
    public IEnumerable<string> GetExternalUrls(BaseItem item)
    {
        var id = item.GetProviderId(Plugin.ProviderIdKey);
        if (string.IsNullOrWhiteSpace(id))
        {
            yield break;
        }

        switch (item)
        {
            case Book:
                yield return string.Format(CultureInfo.InvariantCulture, "https://hardcover.app/id/book/{0}", id);
                break;
            case Person:
                yield return string.Format(CultureInfo.InvariantCulture, "https://hardcover.app/id/author/{0}", id);
                break;
        }
    }
}
