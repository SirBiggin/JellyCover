using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.JellyCover.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.JellyCover;

/// <summary>
/// JellyCover: Hardcover.app metadata provider for Jellyfin book libraries.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// The provider name shown in Jellyfin (library metadata downloaders, external ids, image providers).
    /// </summary>
    public const string ProviderName = "Hardcover";

    /// <summary>
    /// The provider id key stored on items (Book / Person) that links them to a Hardcover record.
    /// </summary>
    public const string ProviderIdKey = "Hardcover";

    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <inheritdoc />
    public override string Name => "JellyCover";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("9be8d848-e1ba-4129-89af-3bf0def71e9c");

    /// <inheritdoc />
    public override string Description => "Book metadata, covers and author details from Hardcover.app.";

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.html", GetType().Namespace)
            }
        ];
    }
}
