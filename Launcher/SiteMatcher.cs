using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CampaignHangar
{
    public static class SiteMatcher
    {
        static readonly HashSet<string> CommonWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "kr", "ko", "kokr", "korean", "en", "enus", "english",
            "한글", "한국어", "번역", "한글번역", "version", "ver", "버전",
            "zip", "mod", "campaign", "project", "캠페인", "프로젝트", "wol", "hots", "lotv", "nco", "sc2"
        };
        sealed class Words
        {
            public readonly string text;
            public readonly HashSet<string> keywords = new HashSet<string>(StringComparer.Ordinal);
            readonly HashSet<int> boundaries = new HashSet<int> { 0 };
            public Words(string value)
            {
                var builder = new StringBuilder();
                string words = Regex.Replace(value ?? "", @"(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", " ");
                foreach (Match match in Regex.Matches(words, @"[\p{L}\p{Nd}]+"))
                {
                    string word = match.Value.ToLowerInvariant();
                    builder.Append(word); boundaries.Add(builder.Length);
                    if (word.Length >= 3 && Regex.IsMatch(word, @"\p{L}") &&
                        !Regex.IsMatch(word, @"^v?\d+$") && !CommonWords.Contains(word))
                        keywords.Add(word);
                }
                text = builder.ToString();
            }
            public bool Contains(string alias)
            {
                string key = new Words(alias).text;
                if (key.Length == 0) return false;
                if (key.Length >= 4 && Regex.IsMatch(key, @"[가-힣]") && text.Contains(key)) return true;
                for (int start = text.IndexOf(key, StringComparison.Ordinal); start >= 0; start = text.IndexOf(key, start + 1, StringComparison.Ordinal))
                    if (boundaries.Contains(start) && boundaries.Contains(start + key.Length)) return true;
                return false;
            }
            public bool Near(string alias)
            {
                string key = new Words(alias).text;
                if (key.Length < 6) return false;
                int tolerance = Math.Min(2, key.Length / 5);
                foreach (int start in boundaries)
                    for (int length = key.Length - tolerance; length <= key.Length + tolerance && start + length <= text.Length; length++)
                        if (boundaries.Contains(start + length) && Distance(key, text.Substring(start, length)) <= tolerance) return true;
                return false;
            }
        }
        static int Distance(string a, string b)
        {
            var rows = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) rows[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) rows[0, j] = j;
            for (int i = 1; i <= a.Length; i++)
                for (int j = 1; j <= b.Length; j++)
                {
                    rows[i, j] = Math.Min(Math.Min(rows[i - 1, j] + 1, rows[i, j - 1] + 1), rows[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
                    if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                        rows[i, j] = Math.Min(rows[i, j], rows[i - 2, j - 2] + 1);
                }
            return rows[a.Length, b.Length];
        }
        static List<Campaign> Strong(Campaign package, IEnumerable<Campaign> catalogue)
        {
            var title = new Words(package.name);
            var filename = new Words(Path.GetFileNameWithoutExtension(package.sourceFileName ?? ""));
            return catalogue.Where(c => c.kind != "resource" && c.kind != "dedicated" && Catalogue.SupportsSlot(c, package.slot) &&
                Names(c).Any(alias => title.Contains(alias) || filename.Contains(alias)))
                .GroupBy(c => c.id).Select(g => g.First()).ToList();
        }
        public static List<Campaign> Candidates(Campaign package, IEnumerable<Campaign> catalogue)
        {
            var sites = catalogue.Where(c => c.kind != "resource" && c.kind != "dedicated" && Catalogue.SupportsSlot(c, package.slot)).GroupBy(c => c.id).Select(g => g.First()).ToList();
            var strong = Strong(package, sites);
            var title = new Words(package.name);
            var filename = new Words(Path.GetFileNameWithoutExtension(package.sourceFileName ?? ""));
            var keywords = new HashSet<string>(title.keywords.Concat(filename.keywords), StringComparer.Ordinal);
            var related = sites.Where(c => !strong.Any(s => s.id == c.id))
                .Select(c => new {
                    campaign = c,
                    matches = Names(c).SelectMany(alias => new Words(alias).keywords).Distinct().Count(keywords.Contains),
                    near = Names(c).Any(alias => title.Near(alias) || filename.Near(alias))
                })
                .Where(c => c.matches > 0 || c.near)
                .OrderByDescending(c => c.matches).Take(5).Select(c => c.campaign);
            return strong.Concat(related).ToList();
        }
        static IEnumerable<string> Names(Campaign item)
        {
            var names = new List<string> { item.id, item.name };
            names.AddRange(item.aliases ?? new string[0]);
            if (item.id == "hehestan") names.Add("로그 헬레스탄");
            if (item.id == "auto-post-6798")names.AddRange(new[]{"협동전 라이크","협동전라이크","RaynorCommander"});
            if (item.id == "ling") names.AddRange(new[] { "Lings of Wiberty", "Ling of Wiberty" });
            if (item.id == "reoverhaul-redux-allies")
                names.AddRange(new[] { "자유의 날개 리오버홀 동맹과 함께", "WoL ReOverhaul ReAllies" });
            return names;
        }
        static string MetadataTitle(Campaign item)
        {
            // Only language labels and separators are presentation, not a different mod.
            string title = Regex.Replace(item.name ?? "", @"(?i)(?:[\s_\-\(\[]+)(?:koKR|enUS|korean|english|kr|ko|en|한글|한국어)(?:[\s\)\]]*)$", "");
            return new Words(title).text;
        }
        static string MetadataAuthor(Campaign item)
        {
            return new Words(item.sourceAuthor).text;
        }
        public static bool SameMetadata(Campaign first, Campaign second)
        {
            return first.slot == second.slot && MetadataTitle(first).Length > 0 &&
                MetadataTitle(first) == MetadataTitle(second) &&
                (String.IsNullOrWhiteSpace(first.sourceAuthor) || String.IsNullOrWhiteSpace(second.sourceAuthor) ||
                    MetadataAuthor(first) == MetadataAuthor(second));
        }
        public static string MetadataId(Campaign item)
        {
            string identity = item.slot + "\n" + MetadataTitle(item) + "\n" + MetadataAuthor(item);
            using (var hash = System.Security.Cryptography.SHA256.Create())
                return "mod-" + BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(identity)))
                    .Replace("-", "").ToLowerInvariant().Substring(0, 24);
        }
        public static bool SearchMatches(Campaign item, string query)
        {
            string key = new Words(query).text;
            return key.Length == 0 || Names(item).Any(alias => new Words(alias).text.Contains(key));
        }
        public static Campaign Unique(Campaign package, IEnumerable<Campaign> catalogue)
        {
            var candidates = Strong(package, catalogue);
            return candidates.Count == 1 ? candidates[0] : null;
        }
    }
}
