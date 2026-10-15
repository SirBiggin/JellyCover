using System.Collections.Generic;
using System.Globalization;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.JellyCover.Providers;

/// <summary>
/// Hardcover author id, shown in the person's external ids.
/// </summary>
public class HardcoverPersonExternalId : IExternalId
{
    /// <inheritdoc />
    public string ProviderName => Plugin.ProviderName;

    /// <inheritdoc />
    public string Key => Plugin.ProviderIdKey;

    /// <inheritdoc />
    public ExternalIdMediaType? Type => ExternalIdMediaType.Person;

    /// <inheritdoc />
    public bool Supports(IHasProviderIds item) => item is Person;
}
