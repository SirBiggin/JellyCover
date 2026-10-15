using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.JellyCover.Api;
using MediaBrowser.Controller.Providers;

namespace Jellyfin.Plugin.JellyCover.Providers;

/// <summary>
/// Title normalisation, search-hit scoring and contributor role mapping shared by the providers.
/// </summary>
internal static partial class Matching
{
    private static readonly string[] _leadingArticles = { "the ", "a ", "an " };

    /// <summary>
    /// Strips file-name noise from a lookup name before it is sent to search:
    /// trailing "(2001)" years, "[tags]", "{tags}" and surrounding whitespace.
    /// </summary>
    /// <param name="name">The raw name.</param>
    /// <returns>The cleaned name.</returns>
    public static string CleanForSearch(string name)
    {
        var s = name.Trim();
        s = BracketedRegex().Replace(s, " ");
        s = TrailingYearRegex().Replace(s, string.Empty);
        s = MultiSpaceRegex().Replace(s, " ").Trim();
        return s.Length == 0 ? name.Trim() : s;
    }

    /// <summary>
    /// Lower-cases, strips diacritics and punctuation, collapses whitespace and drops a leading article.
    /// </summary>
    /// <param name="s">The text.</param>
    /// <returns>The normalised text.</returns>
    public static string Normalize(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            return string.Empty;
        }

        var decomposed = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
            }
            else
            {
                sb.Append(' ');
            }
        }

        var result = MultiSpaceRegex().Replace(sb.ToString(), " ").Trim();
        foreach (var article in _leadingArticles)
        {
            if (result.StartsWith(article, StringComparison.Ordinal) && result.Length > article.Length)
            {
                result = result.Substring(article.Length);
                break;
            }
        }

        return result;
    }

    /// <summary>
    /// Similarity of two titles, 0-100.
    /// </summary>
    /// <param name="a">First title.</param>
    /// <param name="b">Second title.</param>
    /// <returns>The similarity.</returns>
    public static int TitleSimilarity(string? a, string? b)
    {
        var na = Normalize(a);
        var nb = Normalize(b);
        if (na.Length == 0 || nb.Length == 0)
        {
            return 0;
        }

        if (na == nb)
        {
            return 100;
        }

        var ta = na.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var tb = nb.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var intersection = ta.Intersect(tb, StringComparer.Ordinal).Count();
        if (intersection == 0)
        {
            return 0;
        }

        // one title fully contained in the other (e.g. "Dune" vs "Dune Messiah", or title vs title+subtitle)
        if (ta.IsSubsetOf(tb) || tb.IsSubsetOf(ta))
        {
            var ratio = (double)Math.Min(ta.Count, tb.Count) / Math.Max(ta.Count, tb.Count);
            return (int)Math.Round(70 + (25 * ratio));
        }

        var union = ta.Union(tb, StringComparer.Ordinal).Count();
        return (int)Math.Round(85.0 * intersection / union);
    }

    /// <summary>
    /// Scores a search hit against the lookup info (0-100+).
    /// </summary>
    /// <param name="query">The cleaned query title.</param>
    /// <param name="hit">The hit.</param>
    /// <param name="info">The lookup info (year / series are used as tie breakers).</param>
    /// <returns>The score.</returns>
    public static int Score(string query, HardcoverBookSearchHit hit, ItemLookupInfo info)
    {
        var score = TitleSimilarity(query, hit.Title);
        if (!string.IsNullOrWhiteSpace(hit.Subtitle))
        {
            score = Math.Max(score, TitleSimilarity(query, hit.Title + " " + hit.Subtitle));
        }

        if (info.Year is int year && hit.ReleaseYear is int hitYear)
        {
            if (year == hitYear)
            {
                score += 10;
            }
            else if (Math.Abs(year - hitYear) > 2 && score < 100)
            {
                score -= 5;
            }
        }

        if (info is BookInfo book && !string.IsNullOrWhiteSpace(book.SeriesName)
            && hit.SeriesNames.Any(s => Normalize(s) == Normalize(book.SeriesName)))
        {
            score += 10;
        }

        return score;
    }

    /// <summary>
    /// Picks the best hit, or null if nothing reaches <paramref name="minimumScore"/>.
    /// </summary>
    /// <param name="query">The cleaned query title.</param>
    /// <param name="hits">The hits in relevance order.</param>
    /// <param name="info">The lookup info.</param>
    /// <param name="minimumScore">Minimum acceptable score.</param>
    /// <returns>The best hit and its score.</returns>
    public static (HardcoverBookSearchHit? Hit, int Score) Best(string query, IReadOnlyList<HardcoverBookSearchHit> hits, ItemLookupInfo info, int minimumScore)
    {
        HardcoverBookSearchHit? best = null;
        var bestScore = int.MinValue;
        for (var i = 0; i < hits.Count; i++)
        {
            // small relevance bonus so ties go to Hardcover's own ranking
            var s = Score(query, hits[i], info) - i;
            if (s > bestScore)
            {
                best = hits[i];
                bestScore = s;
            }
        }

        return best is not null && bestScore >= minimumScore ? (best, bestScore) : (null, bestScore);
    }

    /// <summary>
    /// Maps a Hardcover contribution label to a Jellyfin person kind.
    /// </summary>
    /// <param name="role">The Hardcover contribution (null / empty = primary author).</param>
    /// <returns>The person kind and the role label to store.</returns>
    public static (PersonKind Kind, string? Role) MapRole(string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            return (PersonKind.Author, null);
        }

        var r = role.Trim();
        var l = r.ToLowerInvariant();
        if (l == "author" || l == "writer" || l == "creator")
        {
            return (PersonKind.Author, null);
        }

        if (l.Contains("illustrat", StringComparison.Ordinal))
        {
            return (PersonKind.Illustrator, r);
        }

        if (l.Contains("translat", StringComparison.Ordinal))
        {
            return (PersonKind.Translator, r);
        }

        if (l.Contains("narrat", StringComparison.Ordinal) || l.Contains("reader", StringComparison.Ordinal))
        {
            return (PersonKind.Narrator, r);
        }

        if (l.Contains("editor", StringComparison.Ordinal))
        {
            return (PersonKind.Editor, r);
        }

        if (l.Contains("cover", StringComparison.Ordinal))
        {
            return (PersonKind.CoverArtist, r);
        }

        if (l.Contains("color", StringComparison.Ordinal) || l.Contains("colour", StringComparison.Ordinal))
        {
            return (PersonKind.Colorist, r);
        }

        if (l.Contains("letter", StringComparison.Ordinal))
        {
            return (PersonKind.Letterer, r);
        }

        if (l.Contains("pencil", StringComparison.Ordinal))
        {
            return (PersonKind.Penciller, r);
        }

        if (l.Contains("ink", StringComparison.Ordinal))
        {
            return (PersonKind.Inker, r);
        }

        if (l.Contains("artist", StringComparison.Ordinal) || l.Contains("art", StringComparison.Ordinal))
        {
            return (PersonKind.Artist, r);
        }

        // foreword, afterword, introduction, contributor, ghostwriter...
        return (PersonKind.Writer, r);
    }

    [GeneratedRegex(@"[\[\{][^\]\}]*[\]\}]")]
    private static partial Regex BracketedRegex();

    [GeneratedRegex(@"\s*\((?:19|20)\d{2}\)\s*$")]
    private static partial Regex TrailingYearRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex MultiSpaceRegex();
}
