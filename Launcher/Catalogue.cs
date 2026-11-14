using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CampaignHangar
{
    public static class Catalogue
    {
        public static readonly string[] Keys = { "wol", "hots", "lotv", "nco", "other" };
        public static readonly string[] Titles = { "자날", "군심", "공유", "노바", "기타 캠페인" };
        public static readonly string[] SiteKeys = { "gallery", "rogue", "translated", "untranslated" };
        public static readonly string[] SiteTitles = { "갤산", "로그라이크", "한글 번역", "미번역" };
        public static string Group(Campaign item)
        {
            return GroupName(item.@base);
        }
        static string GroupName(string name)
        {
            switch (name)
            {
                case "자유의 날개": return "wol";
                case "군단의 심장": return "hots";
                case "공허의 유산": return "lotv";
                case "노바 비밀 작전": return "nco";
                default: return "other";
            }
        }
        public static bool SupportsSlot(Campaign item, string slot)
        {
            return item.campaignGroups != null && item.campaignGroups.Length > 0
                ? item.campaignGroups.Any(group => GroupName(group) == slot)
                : Group(item) == slot || Group(item) == "other";
        }
        static bool InGroup(Campaign item, string category, string slot)
        {
            if (category == "gallery" && !String.IsNullOrWhiteSpace(item.galleryGroup)) return GroupName(item.galleryGroup) == slot;
            if (item.campaignGroups != null && item.campaignGroups.Length > 0) return item.campaignGroups.Any(group => GroupName(group) == slot);
            return Group(item) == slot;
        }
        public static Campaign ToggleSelection(Campaign current, Campaign clicked)
        {
            return current != null && current.id == clicked.id ? null : clicked;
        }
        public static Campaign ArchiveDownload(IEnumerable<Campaign> items, string url)
        {
            if (!Data.DirectZip(url)) return null;
            return DistributionPolicy.Resolve(items, url, null);
        }
        public static IEnumerable<Campaign> Filter(IEnumerable<Campaign> items, string category, string query, string slot = null)
        {
            return items.Where(c => (c.categories ?? new string[0]).Contains(category) &&
                (slot == null || InGroup(c, category, slot)) &&
                c.name.IndexOf(query ?? "", StringComparison.CurrentCultureIgnoreCase) >= 0);
        }
        public static string DetailUrl(Campaign item)
        {
            string category = item.kind == "dedicated" ? "dedicated" :
                (item.categories ?? new string[0]).FirstOrDefault(c => SiteKeys.Contains(c)) ?? "gallery";
            return Data.Site + "/campaigns.html?campaign=" + Uri.EscapeDataString(item.id) + "#" + category;
        }
        public static string ThumbnailUrl(Campaign item)
        {
            string path = item.thumbnail ?? (item.images ?? new string[0]).FirstOrDefault();
            if (String.IsNullOrWhiteSpace(path)) return null;
            Uri uri;
            if (!Uri.TryCreate(new Uri(Data.Site + "/"), path, out uri) || uri.Scheme != "https" ||
                !uri.IsDefaultPort || !String.IsNullOrEmpty(uri.UserInfo)) return null;
            // The archive keeps author-provided campaign thumbnails on ForgeCDN.
            // Trust only its exact media hostname, never arbitrary site supplied
            // URLs or similar-looking subdomains; other images remain site-local.
            bool archive = uri.Host.Equals(new Uri(Data.Site).Host, StringComparison.OrdinalIgnoreCase);
            bool forge = uri.Host.Equals("media.forgecdn.net", StringComparison.OrdinalIgnoreCase) &&
                String.IsNullOrEmpty(uri.Query) && String.IsNullOrEmpty(uri.Fragment) &&
                new[] { ".png", ".jpg", ".jpeg", ".webp", ".gif" }
                    .Contains(Path.GetExtension(uri.AbsolutePath), StringComparer.OrdinalIgnoreCase);
            if (!archive && !forge) return null;
            return uri.AbsoluteUri;
        }
        static string VersionLabel(string value)
        {
            string label = (value ?? "").Split('·')[0].Trim();
            if (label.StartsWith("ReAllies ", StringComparison.OrdinalIgnoreCase)) label = label.Substring("ReAllies ".Length).Trim();
            return label.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? label.Substring(1) : label;
        }
        public static bool SameVersion(string site, string installed)
        {
            string a = VersionLabel(site), b = VersionLabel(installed);
            Version va, vb;
            if (Version.TryParse(a, out va) && Version.TryParse(b, out vb))
                return va.Major == vb.Major && va.Minor == vb.Minor &&
                    Math.Max(0, va.Build) == Math.Max(0, vb.Build) && Math.Max(0, va.Revision) == Math.Max(0, vb.Revision);
            return !String.IsNullOrEmpty(a) && a.Equals(b, StringComparison.OrdinalIgnoreCase);
        }
        public static bool NewerVersion(string site, string installed)
        {
            string a = VersionLabel(site), b = VersionLabel(installed);
            if (!System.Text.RegularExpressions.Regex.IsMatch(a, @"^\d+(?:\.\d+){0,3}$") ||
                !System.Text.RegularExpressions.Regex.IsMatch(b, @"^\d+(?:\.\d+){0,3}$")) return false;
            string[] left = a.Split('.'), right = b.Split('.');
            var x = new ulong[4]; var y = new ulong[4];
            for (int i = 0; i < left.Length; i++) if (!UInt64.TryParse(left[i], out x[i])) return false;
            for (int i = 0; i < right.Length; i++) if (!UInt64.TryParse(right[i], out y[i])) return false;
            for (int i = 0; i < 4; i++) { if (x[i] != y[i]) return x[i] > y[i]; }
            return false;
        }
    }
}
