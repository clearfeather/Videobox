#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Screenbox.Core.Enums;
using Screenbox.Core.ViewModels;

namespace Screenbox.Core.Helpers;

public static class MediaTitleFormatter
{
    private static readonly char[] Separators = { '_', '.', '-', ' ' };
    private static readonly HashSet<string> KnownStudioPrefixes = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TextInfo TextInfo = CultureInfo.CurrentCulture.TextInfo;

    public static string GetDisplayName(MediaViewModel media, bool enabled)
    {
        if (!enabled || media.MediaType != MediaPlaybackType.Video)
        {
            return media.Name;
        }

        string[] tokens = GetTitleTokens(media.Name);
        if (tokens.Length == 0)
        {
            return media.Name;
        }

        LearnPathStudioPrefixes(media.Location);
        tokens = RemoveStudioPrefix(tokens);
        tokens = RemoveDateTokens(tokens);
        tokens = tokens.Where(token => GetQualityTag(token) == null).ToArray();

        string title = string.Join(" ", tokens.Select(CleanWord));
        title = Regex.Replace(title, @"\s+", " ").Trim();

        return string.IsNullOrWhiteSpace(title)
            ? media.Name
            : TextInfo.ToTitleCase(title.ToLower(CultureInfo.CurrentCulture));
    }

    public static string GetDisplayCaption(MediaViewModel media, bool enabled)
    {
        string caption = media.Caption;
        if (!enabled || media.MediaType != MediaPlaybackType.Video)
        {
            return caption;
        }

        string? qualityTag = GetQualityTag(media);
        if (qualityTag == null)
        {
            return caption;
        }

        return string.IsNullOrWhiteSpace(caption)
            ? qualityTag
            : $"{qualityTag} - {caption}";
    }

    public static void LearnCommonStudioPrefixes(IEnumerable<MediaViewModel> mediaItems, IEnumerable<string>? contextNames = null)
    {
        if (contextNames != null)
        {
            foreach (string contextName in contextNames)
            {
                string normalizedContextName = NormalizeName(contextName);
                if (IsStudioPrefixCandidate(normalizedContextName))
                {
                    KnownStudioPrefixes.Add(normalizedContextName);
                }

                foreach (string token in GetTitleTokens(contextName).Select(NormalizeToken).Where(IsStudioPrefixCandidate))
                {
                    KnownStudioPrefixes.Add(token);
                }
            }
        }

        List<string> candidates = mediaItems
            .Where(media => media.MediaType == MediaPlaybackType.Video)
            .Select(media => GetTitleTokens(media.Name))
            .Where(tokens => tokens.Length >= 3)
            .Select(tokens => NormalizeToken(tokens[0]))
            .Where(IsStudioPrefixCandidate)
            .ToList();

        if (candidates.Count < 2)
        {
            return;
        }

        foreach (IGrouping<string, string> group in candidates.GroupBy(value => value))
        {
            if (group.Count() >= Math.Max(2, candidates.Count * 0.6))
            {
                KnownStudioPrefixes.Add(group.Key);
            }
        }
    }

    private static string[] RemoveStudioPrefix(string[] tokens)
    {
        if (tokens.Length < 3)
        {
            return tokens;
        }

        List<string> remaining = tokens.ToList();
        while (remaining.Count > 1 && IsKnownStudioToken(remaining[0]))
        {
            remaining.RemoveAt(0);
        }

        return remaining.ToArray();
    }

    private static string[] RemoveDateTokens(string[] tokens)
    {
        List<string> result = new();
        for (int i = 0; i < tokens.Length; i++)
        {
            string value = NormalizeToken(tokens[i]);
            if (IsCompactDate(value))
            {
                continue;
            }

            if (i + 2 < tokens.Length)
            {
                string first = NormalizeToken(tokens[i]);
                string second = NormalizeToken(tokens[i + 1]);
                string third = NormalizeToken(tokens[i + 2]);
                if ((IsYear(first) && IsMonth(second) && IsDay(third)) ||
                    (IsMonth(first) && IsDay(second) && IsYear(third)))
                {
                    i += 2;
                    continue;
                }
            }

            result.Add(tokens[i]);
        }

        return result.ToArray();
    }

    private static string[] GetTitleTokens(string rawName)
    {
        string title = Path.GetFileNameWithoutExtension(rawName);
        if (string.IsNullOrWhiteSpace(title))
        {
            title = rawName;
        }

        title = Regex.Replace(title, @"[\[\]\(\)\{\}]", " ");
        title = Regex.Replace(title, @"(?<=[\p{Ll}\p{Nd}])(?=\p{Lu})", " ");
        return title.Split(Separators, StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Trim())
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .ToArray();
    }

    private static string? GetQualityTag(IEnumerable<string> tokens)
    {
        foreach (string token in tokens)
        {
            if (GetQualityTag(token) is { } tag)
            {
                return tag;
            }
        }

        return null;
    }

    private static string? GetQualityTag(MediaViewModel media)
    {
        if (GetQualityTag(GetTitleTokens(media.Name)) is { } fileNameTag)
        {
            return fileNameTag;
        }

        uint width = media.MediaInfo.VideoProperties.Width;
        uint height = media.MediaInfo.VideoProperties.Height;
        if (width == 0 || height == 0)
        {
            return null;
        }

        uint shortSide = Math.Min(width, height);
        return shortSide switch
        {
            >= 2160 => "4K",
            >= 1440 => "1440p",
            >= 1080 => "1080p",
            >= 720 => "720p",
            >= 576 => "576p",
            >= 480 => "480p",
            >= 360 => "360p",
            _ => $"{shortSide}p"
        };
    }

    private static string? GetQualityTag(string token)
    {
        string value = NormalizeToken(token);
        return value switch
        {
            "3820" or "3840" or "2160" or "2160p" or "4k" or "uhd" => "4K",
            "2560" or "1440" or "1440p" or "2k" or "qhd" => "1440p",
            "1920" or "1080" or "1080p" => "1080p",
            "1280" or "720" or "720p" => "720p",
            "1024" or "576" or "576p" => "576p",
            "854" or "852" or "848" or "720x480" or "480" or "480p" => "480p",
            "640" or "360" or "360p" => "360p",
            "426" or "424" or "240" or "240p" => "240p",
            _ => null
        };
    }

    private static string CleanWord(string token)
    {
        return Regex.Replace(token, @"[^\p{L}\p{Nd}'&]+", " ").Trim();
    }

    private static string NormalizeToken(string token)
    {
        return Regex.Replace(token.Trim().ToLowerInvariant(), @"[^\p{L}\p{Nd}]+", string.Empty);
    }

    private static string NormalizeName(string name)
    {
        return Regex.Replace(Path.GetFileNameWithoutExtension(name).Trim().ToLowerInvariant(), @"[^\p{L}\p{Nd}]+", string.Empty);
    }

    private static void LearnPathStudioPrefixes(string location)
    {
        if (string.IsNullOrWhiteSpace(location))
        {
            return;
        }

        string path = location;
        if (Uri.TryCreate(location, UriKind.Absolute, out Uri uri) && uri.IsFile)
        {
            path = uri.LocalPath;
        }

        string? directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        string[] pathSegments = directory.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);

        foreach (string segment in Enumerable.Reverse(pathSegments).Take(3))
        {
            string normalizedSegment = NormalizeName(segment);
            if (IsStudioPrefixCandidate(normalizedSegment))
            {
                KnownStudioPrefixes.Add(normalizedSegment);
            }

            foreach (string token in GetTitleTokens(segment).Select(NormalizeToken).Where(IsStudioPrefixCandidate))
            {
                KnownStudioPrefixes.Add(token);
            }
        }
    }

    private static bool IsStudioPrefixCandidate(string token)
    {
        return token.Length >= 2 &&
               token.Any(char.IsLetter) &&
               token.All(char.IsLetterOrDigit) &&
               GetQualityTag(token) == null;
    }

    private static bool IsKnownStudioToken(string token)
    {
        string candidate = NormalizeToken(token);
        return KnownStudioPrefixes.Contains(candidate) ||
               KnownStudioPrefixes.Any(prefix => prefix.Length > candidate.Length && prefix.Contains(candidate));
    }

    private static bool IsCompactDate(string value)
    {
        return value.Length == 8 &&
               IsYear(value.Substring(0, 4)) &&
               int.TryParse(value.Substring(4, 2), NumberStyles.None, CultureInfo.InvariantCulture, out int month) &&
               int.TryParse(value.Substring(6, 2), NumberStyles.None, CultureInfo.InvariantCulture, out int day) &&
               month is >= 1 and <= 12 &&
               day is >= 1 and <= 31;
    }

    private static bool IsYear(string value)
    {
        return value.Length == 4 &&
               int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int year) &&
               year is >= 1900 and <= 2099;
    }

    private static bool IsMonth(string value)
    {
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int month) &&
               month is >= 1 and <= 12;
    }

    private static bool IsDay(string value)
    {
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int day) &&
               day is >= 1 and <= 31;
    }
}
