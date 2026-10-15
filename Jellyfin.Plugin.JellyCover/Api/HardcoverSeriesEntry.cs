using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyCover.Api;

/// <summary>
/// A series membership of a book.
/// </summary>
public sealed class HardcoverSeriesEntry
{
    /// <summary>
    /// Gets or sets the Hardcover series id.
    /// </summary>
    public int SeriesId { get; set; }

    /// <summary>
    /// Gets or sets the series name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the numeric position in the series.
    /// </summary>
    public double? Position { get; set; }

    /// <summary>
    /// Gets or sets the text form of the position (e.g. "1.5").
    /// </summary>
    public string? Details { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this is the featured series.
    /// </summary>
    public bool Featured { get; set; }
}
