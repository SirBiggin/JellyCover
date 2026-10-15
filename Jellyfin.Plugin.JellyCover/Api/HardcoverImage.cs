using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyCover.Api;

/// <summary>
/// A cover / photo hosted by Hardcover.
/// </summary>
public sealed class HardcoverImage
{
    /// <summary>
    /// Gets or sets the image url.
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the width in pixels, if known.
    /// </summary>
    public int? Width { get; set; }

    /// <summary>
    /// Gets or sets the height in pixels, if known.
    /// </summary>
    public int? Height { get; set; }
}
