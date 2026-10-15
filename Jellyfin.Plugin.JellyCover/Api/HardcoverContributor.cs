using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyCover.Api;

/// <summary>
/// A contributor credit on a book.
/// </summary>
public sealed class HardcoverContributor
{
    /// <summary>
    /// Gets or sets the Hardcover author id.
    /// </summary>
    public int AuthorId { get; set; }

    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the url slug.
    /// </summary>
    public string? Slug { get; set; }

    /// <summary>
    /// Gets or sets the role as stored on Hardcover (null / empty means primary author).
    /// </summary>
    public string? Role { get; set; }

    /// <summary>
    /// Gets or sets the author photo url.
    /// </summary>
    public string? ImageUrl { get; set; }
}
