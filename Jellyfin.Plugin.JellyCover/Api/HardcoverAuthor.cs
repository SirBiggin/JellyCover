using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.JellyCover.Api;

/// <summary>
/// Full author details from Hardcover.
/// </summary>
public sealed class HardcoverAuthor
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
    /// Gets or sets the biography.
    /// </summary>
    public string? Bio { get; set; }

    /// <summary>
    /// Gets or sets the birth date.
    /// </summary>
    public DateTime? BornDate { get; set; }

    /// <summary>
    /// Gets or sets the death date.
    /// </summary>
    public DateTime? DeathDate { get; set; }

    /// <summary>
    /// Gets or sets the location.
    /// </summary>
    public string? Location { get; set; }

    /// <summary>
    /// Gets or sets the number of books.
    /// </summary>
    public int BooksCount { get; set; }

    /// <summary>
    /// Gets or sets the photo.
    /// </summary>
    public HardcoverImage? Image { get; set; }
}
