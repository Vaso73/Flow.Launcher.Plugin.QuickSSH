using System;
using System.Globalization;
using System.Text;

namespace Flow.Launcher.Plugin.QuickSSH
{
    /// <summary>
    /// Provides accent-insensitive and fuzzy matching for saved profile searches.
    /// </summary>
    internal static class SearchMatcher
    {
        internal static int ScoreProfile(string search, string name, string command)
        {
            var searchLower = RemoveDiacritics(search.ToLowerInvariant());
            var nameLower = RemoveDiacritics(name.ToLowerInvariant());
            var commandLower = RemoveDiacritics(command.ToLowerInvariant());

            bool nameExact = ContainsIgnoreAccents(nameLower, searchLower);
            bool cmdExact = ContainsIgnoreAccents(commandLower, searchLower);

            if (nameExact && cmdExact) return 0;
            if (nameExact) return 1;
            if (cmdExact) return 2;

            bool nameFuzzy = FuzzyContains(nameLower, searchLower);
            bool cmdFuzzy = FuzzyContains(commandLower, searchLower);

            if (nameFuzzy && cmdFuzzy) return 3;
            if (nameFuzzy) return 4;
            if (cmdFuzzy) return 5;

            return int.MaxValue;
        }

        internal static bool ContainsIgnoreAccents(string source, string search)
        {
            return RemoveDiacritics(source).Contains(RemoveDiacritics(search));
        }

        private static string RemoveDiacritics(string text)
        {
            var normalized = text.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(normalized.Length);
            foreach (var c in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            }

            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        private static bool FuzzyContains(string source, string search)
        {
            if (search.Length < 5)
                return source.Contains(search);

            int tolerance = search.Length / 5;

            for (int i = 0; i <= source.Length - search.Length + tolerance; i++)
            {
                int end = Math.Min(i + search.Length + tolerance, source.Length);
                var window = source.Substring(i, end - i);
                if (DamerauLevenshteinDistance(window, search) <= tolerance)
                    return true;
            }

            return false;
        }

        private static int DamerauLevenshteinDistance(string s, string t)
        {
            int n = s.Length;
            int m = t.Length;
            var d = new int[n + 1, m + 1];

            for (int i = 0; i <= n; i++) d[i, 0] = i;
            for (int j = 0; j <= m; j++) d[0, j] = j;

            for (int i = 1; i <= n; i++)
            {
                for (int j = 1; j <= m; j++)
                {
                    int cost = s[i - 1] == t[j - 1] ? 0 : 1;

                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);

                    if (i > 1 && j > 1 && s[i - 1] == t[j - 2] && s[i - 2] == t[j - 1])
                        d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + cost);
                }
            }

            return d[n, m];
        }
    }
}
