using System;

using VSIXBundler.Core.Installer;

namespace VSIXBundler.Core
{
    public interface ISettings
    {
        string VsixName { get; }
        string LiveFeedUrl { get; }
        string LiveFeedCachePath { get; }
        string LogFilePath { get; set; }
        double UpdateIntervalDays { get; }
        string RegistrySubKey { get; }
        IResourceProvider ResourceProvider { get; }
    }

    public class Settings : ISettings
    {
        public string VsixName { get; }
        public string LiveFeedUrl { get; }

        public Settings(IResourceProvider resourceProvider) : this("", "", "", resourceProvider)
        {
        }

        public Settings(string vsixName, string liveFeedUrl, string registrySubKey, IResourceProvider resourceProvider)
        {
            if (resourceProvider == null) throw new ArgumentNullException(nameof(resourceProvider));

            VsixName = vsixName;
            LiveFeedUrl = liveFeedUrl;
            RegistrySubKey = registrySubKey;
            ResourceProvider = resourceProvider;

            LiveFeedCachePath = Environment.ExpandEnvironmentVariables("%localAppData%\\" + VsixName + "\\feed.json");
            LogFilePath = Environment.ExpandEnvironmentVariables("%localAppData%\\" + VsixName + "\\installer.log");
        }

        //public const string LiveFeedUrl = "https://rawgit.com/madskristensen/WebEssentials2017/master/extensions.json";
        public string LiveFeedCachePath { get; set; }

        public string LogFilePath { get; set; }

        public double UpdateIntervalDays { get; } = 1;
        public string RegistrySubKey { get; }
        public IResourceProvider ResourceProvider { get; }
    }
}