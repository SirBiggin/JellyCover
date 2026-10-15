using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyCover.Providers;

/// <summary>
/// Extra lookup clues gathered from the book's own folder: a Calibre / generic <c>metadata.opf</c> sidecar
/// (exact title, authors, ISBN, ASIN, series), or failing that the Calibre folder layout
/// (<c>Author/Title (id)/Title - Author.ext</c>) or a plain <c>Title - Author</c> file name.
/// Jellyfin's item name is often the truncated file name, so these make the difference between a match and a miss.
/// </summary>
internal sealed partial class LookupHints
{
    private static readonly XNamespace _dc = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace _opf = "http://www.idpf.org/2007/opf";
    private static readonly string[] _authorSeparators = { " & ", " and ", ", ", ";" };

    private LookupHints()
    {
    }

    /// <summary>
    /// Gets the exact title, if one was found.
    /// </summary>
    public string? Title { get; private set; }

    /// <summary>
    /// Gets the author names, if any were found.
    /// </summary>
    public IList<string> Authors { get; } = new List<string>();

    /// <summary>
    /// Gets the ISBN-10 / ISBN-13 (digits only), if one was found.
    /// </summary>
    public string? Isbn { get; private set; }

    /// <summary>
    /// Gets the Amazon ASIN, if one was found.
    /// </summary>
    public string? Asin { get; private set; }

    /// <summary>
    /// Gets the series name, if one was found.
    /// </summary>
    public string? SeriesName { get; private set; }

    /// <summary>
    /// Gets the position in the series, if it is a whole number.
    /// </summary>
    public int? SeriesIndex { get; private set; }

    /// <summary>
    /// Gets where the hints came from: "opf", "path", "name" or "none".
    /// </summary>
    public string Source { get; private set; } = "none";

    /// <summary>
    /// Gets a value indicating whether anything useful was found.
    /// </summary>
    public bool HasAny => Title is not null || Authors.Count > 0 || Isbn is not null || Asin is not null;

    /// <summary>
    /// Collects hints for a book file.
    /// </summary>
    /// <param name="path">The book file path (may be null for items without a file).</param>
    /// <param name="name">Jellyfin's current name for the item.</param>
    /// <param name="readSidecar">Whether an OPF sidecar may be read.</param>
    /// <param name="logger">Logger for parse problems.</param>
    /// <returns>The hints (possibly empty, never null).</returns>
    public static LookupHints Collect(string? path, string? name, bool readSidecar, ILogger logger)
    {
        var hints = new LookupHints();
        try
        {
            if (readSidecar && !string.IsNullOrWhiteSpace(path))
            {
                var dir = Path.GetDirectoryName(path);
                if (dir is not null)
                {
                    foreach (var candidate in new[] { Path.ChangeExtension(path, ".opf"), Path.Combine(dir, "metadata.opf") })
                    {
                        if (File.Exists(candidate) && hints.ReadOpf(candidate, logger))
                        {
                            hints.Source = "opf";
                            break;
                        }
                    }
                }
            }

            if (hints.Title is null && !string.IsNullOrWhiteSpace(path) && hints.FromCalibrePath(path))
            {
                hints.Source = "path";
            }

            if (hints.Title is null && !string.IsNullOrWhiteSpace(name) && hints.FromTitleAuthorName(name))
            {
                hints.Source = "name";
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not collect lookup hints for {Path}", path);
        }

        return hints;
    }

    /// <summary>
    /// Keeps only ISBN digits (and a check digit X); returns null unless the result is 10 or 13 characters.
    /// </summary>
    /// <param name="value">The raw value.</param>
    /// <returns>The cleaned ISBN or null.</returns>
    public static string? CleanIsbn(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var v = value.Trim();
        if (v.StartsWith("urn:isbn:", StringComparison.OrdinalIgnoreCase))
        {
            v = v.Substring(9);
        }

        var clean = new string(v.Where(c => char.IsDigit(c) || c == 'X' || c == 'x').ToArray()).ToUpperInvariant();
        return clean.Length == 10 || clean.Length == 13 ? clean : null;
    }

    /// <summary>
    /// Validates an ASIN (10 upper-case alphanumerics, Amazon's book ASINs start with B or are an ISBN-10).
    /// </summary>
    /// <param name="value">The raw value.</param>
    /// <returns>The ASIN or null.</returns>
    public static string? CleanAsin(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var v = value.Trim().ToUpperInvariant();
        return AsinRegex().IsMatch(v) ? v : null;
    }

    private bool ReadOpf(string file, ILogger logger)
    {
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true
            };

            XDocument doc;
            using (var stream = File.OpenRead(file))
            using (var reader = XmlReader.Create(stream, settings))
            {
                doc = XDocument.Load(reader);
            }

            var metadata = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "metadata") ?? doc.Root;
            if (metadata is null)
            {
                return false;
            }

            Title = metadata.Elements(_dc + "title").Select(e => e.Value.Trim()).FirstOrDefault(t => t.Length > 0);

            foreach (var creator in metadata.Elements(_dc + "creator"))
            {
                var role = (string?)creator.Attribute(_opf + "role");
                if (!string.IsNullOrEmpty(role) && !string.Equals(role, "aut", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var author = creator.Value.Trim();
                if (author.Length > 0 && !Authors.Contains(author, StringComparer.OrdinalIgnoreCase))
                {
                    Authors.Add(author);
                }
            }

            foreach (var identifier in metadata.Elements(_dc + "identifier"))
            {
                var scheme = ((string?)identifier.Attribute(_opf + "scheme") ?? string.Empty).Trim();
                var value = identifier.Value.Trim();
                if (Isbn is null && (string.Equals(scheme, "ISBN", StringComparison.OrdinalIgnoreCase) || value.StartsWith("urn:isbn:", StringComparison.OrdinalIgnoreCase)))
                {
                    Isbn = CleanIsbn(value);
                }
                else if (Asin is null && (scheme.Contains("ASIN", StringComparison.OrdinalIgnoreCase) || string.Equals(scheme, "AMAZON", StringComparison.OrdinalIgnoreCase)))
                {
                    Asin = CleanAsin(value);
                }
            }

            foreach (var meta in metadata.Elements().Where(e => e.Name.LocalName == "meta"))
            {
                var metaName = (string?)meta.Attribute("name");
                var content = (string?)meta.Attribute("content");
                if (string.IsNullOrWhiteSpace(content))
                {
                    continue;
                }

                if (string.Equals(metaName, "calibre:series", StringComparison.OrdinalIgnoreCase))
                {
                    SeriesName = content.Trim();
                }
                else if (string.Equals(metaName, "calibre:series_index", StringComparison.OrdinalIgnoreCase)
                    && double.TryParse(content, NumberStyles.Float, CultureInfo.InvariantCulture, out var index)
                    && index > 0 && index < int.MaxValue && Math.Abs(index - Math.Round(index)) < 0.001)
                {
                    SeriesIndex = (int)Math.Round(index);
                }
            }

            return Title is not null || Authors.Count > 0 || Isbn is not null || Asin is not null;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not read OPF sidecar {File}", file);
            return false;
        }
    }

    // Calibre layout: <library>/<Author>/<Title> (<id>)/<Title truncated> - <Author truncated>.<ext>
    private bool FromCalibrePath(string path)
    {
        var fileName = Path.GetFileNameWithoutExtension(path);
        var split = TitleAuthorRegex().Match(fileName);
        if (!split.Success)
        {
            return false;
        }

        var dir = Path.GetDirectoryName(path);
        var parentName = dir is null ? null : Path.GetFileName(dir);
        if (string.IsNullOrEmpty(parentName))
        {
            return false;
        }

        var folderTitle = CalibreIdSuffixRegex().Replace(parentName, string.Empty).Trim();
        var fileTitle = split.Groups["title"].Value.Trim();
        var fileAuthor = split.Groups["author"].Value.Trim();
        if (folderTitle.Length < fileTitle.Length || !folderTitle.StartsWith(fileTitle, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        Title = folderTitle;

        var grandparent = dir is null ? null : Path.GetDirectoryName(dir);
        var authorFolder = grandparent is null ? null : Path.GetFileName(grandparent);
        if (!string.IsNullOrEmpty(authorFolder)
            && (authorFolder.StartsWith(fileAuthor, StringComparison.OrdinalIgnoreCase) || fileAuthor.StartsWith(authorFolder, StringComparison.OrdinalIgnoreCase)))
        {
            AddAuthors(authorFolder);
        }
        else
        {
            AddAuthors(fileAuthor);
        }

        return true;
    }

    // "Title - Author" file names outside a Calibre layout; only when the right-hand side looks like a person's name
    private bool FromTitleAuthorName(string name)
    {
        var split = TitleAuthorRegex().Match(Path.GetFileNameWithoutExtension(name.Trim()));
        if (!split.Success)
        {
            return false;
        }

        var author = split.Groups["author"].Value.Trim();
        if (!PersonNameRegex().IsMatch(author))
        {
            return false;
        }

        Title = split.Groups["title"].Value.Trim();
        AddAuthors(author);
        return true;
    }

    private void AddAuthors(string value)
    {
        foreach (var part in value.Split(_authorSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.Length > 0 && !Authors.Contains(part, StringComparer.OrdinalIgnoreCase))
            {
                Authors.Add(part);
            }
        }
    }

    [GeneratedRegex(@"^(?<title>.+?)\s+-\s+(?<author>[^-]+)$")]
    private static partial Regex TitleAuthorRegex();

    [GeneratedRegex(@"\s*\(\d+\)\s*$")]
    private static partial Regex CalibreIdSuffixRegex();

    // 2-4 words, letters / apostrophes / dots / hyphens only, no digits: "Mike Mignola", "J. R. R. Tolkien", "Ursula K. Le Guin"
    [GeneratedRegex(@"^\p{L}[\p{L}'.\-]*(?:\s+\p{L}[\p{L}'.\-]*){1,3}$")]
    private static partial Regex PersonNameRegex();

    [GeneratedRegex(@"^[A-Z0-9]{10}$")]
    private static partial Regex AsinRegex();
}
