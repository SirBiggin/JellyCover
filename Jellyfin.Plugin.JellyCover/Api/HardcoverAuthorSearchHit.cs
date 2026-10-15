using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyCover.Api;

/// <summary>
/// A hit from the Hardcover author search (Typesense document).
/// </summary>
public sealed class HardcoverAuthorSearchHit
{
    /// <summary>
    /// Gets or sets the Hardcover author id.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Gets or sets the name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the url slug.
    /// </summary>
    public string? Slug { get; set; }

    /// <summary>
    /// Gets or sets the photo url.
    /// </summary>
    public string? ImageUrl { get; set; }

    /// <summary>
    /// Gets or sets the number of books.
    /// </summary>
    public int BooksCount { get; set; }

    /// <summary>
    /// Gets the alternate names.
    /// </summary>
    public IList<string> AlternateNames { get; } = new List<string>();

    /// <summary>
    /// Gets the titles of the author's most popular books.
    /// </summary>
    public IList<string> Books { get; } = new List<string>();
}
