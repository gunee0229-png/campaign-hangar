using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CampaignHangar
{
    public static class DistributionPolicy
    {
        public const long MaximumBytes = 4L * 1024 * 1024 * 1024;
        public static bool Https(string value)
        {
            Uri uri;
            return Uri.TryCreate(value, UriKind.Absolute, out uri) && uri.Scheme == "https" && uri.IsDefaultPort && String.IsNullOrEmpty(uri.UserInfo);
        }
        public static List<CampaignDownload> Links(Campaign campaign)
        {
            if (campaign == null || campaign.kind == "resource") return new List<CampaignDownload>();
            return new[] { new CampaignDownload { url = campaign.download, label = "배포 파일" } }
                .Concat(campaign.downloads ?? new List<CampaignDownload>()).Where(link => link != null && Https(link.url))
                .GroupBy(link => new Uri(link.url).GetComponents(UriComponents.HttpRequestUrl, UriFormat.UriEscaped), StringComparer.Ordinal)
                .Select(group => group.First()).ToList();
        }
        public static Campaign Resolve(IEnumerable<Campaign> catalogue, string url, string campaignId)
        {
            if (!Https(url)) return null;
            string target = new Uri(url).GetComponents(UriComponents.HttpRequestUrl, UriFormat.UriEscaped);
            var items = catalogue.Where(c => c.kind != "resource" && (String.IsNullOrEmpty(campaignId) || c.id == campaignId) &&
                Links(c).Any(link => new Uri(link.url).GetComponents(UriComponents.HttpRequestUrl, UriFormat.UriEscaped) == target))
                .GroupBy(c => c.id).Select(g => g.First()).ToList();
            return items.Count == 1 ? items[0] : null;
        }
        public static bool DrivePage(string value)
        {
            if (!Https(value)) return false;
            return new[] { "drive.google.com", "docs.google.com", "accounts.google.com", "drive.usercontent.google.com" }.Contains(new Uri(value).Host);
        }
        public static bool DriveDownload(string source, string download, string filename)
        {
            if (!DrivePage(source) || !Https(download) || !String.Equals(Path.GetExtension(filename), ".zip", StringComparison.OrdinalIgnoreCase)) return false;
            string host = new Uri(download).Host;
            return DrivePage(download) || host == "googleusercontent.com" || host.EndsWith(".googleusercontent.com", StringComparison.Ordinal);
        }
    }
}
