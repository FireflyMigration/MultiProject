using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;

using Newtonsoft.Json.Linq;

using VSIXBundler.Core.Helpers;

namespace VSIXBundler.Core.Installer
{
    public class LiveFeed
    {
        private readonly ILogger _logger;

        public LiveFeed(string liveFeedUrl, string cachePath, ILogger logger)
        {
            _logger = logger;
            LocalCachePath = cachePath;
            LiveFeedUrl = liveFeedUrl;
            Extensions = new List<ExtensionEntry>();
        }

        public string LocalCachePath { get; }
        public string LiveFeedUrl { get; }
        public List<ExtensionEntry> Extensions { get; }

        public async Task<bool> UpdateAsync()
        {
            bool hasUpdates = await DownloadFileAsync();
            await ParseAsync();

            return hasUpdates;
        }

        public void Reset()
        {
            try
            {
                File.Delete(LocalCachePath);
            }
            catch (Exception ex)
            {
                _logger.Log(ex.ToString());
            }
        }

        public async Task ParseAsync()
        {
            if (!File.Exists(LocalCachePath))
                return;

            try
            {
                using (var reader = new StreamReader(LocalCachePath))
                {
                    string json = await reader.ReadToEndAsync();
                    var root = JObject.Parse(json);

                    foreach (JProperty obj in root.Children<JProperty>())
                    {
                        JEnumerable<JProperty> child = obj.Children<JProperty>();

                        var entry = new ExtensionEntry()
                        {
                            Name = obj.Name,
                            Id = (string)root[obj.Name]["id"],
                            MinVersion = new Version((string)root[obj.Name]["minVersion"] ?? "15.0"),
                            MaxVersion = new Version((string)root[obj.Name]["maxVersion"] ?? "16.0")
                        };

                        Extensions.Add(entry);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.Write(ex);
            }
        }

        private async Task<bool> DownloadFileAsync()
        {
            string oldContent = File.Exists(LocalCachePath) ? File.ReadAllText(LocalCachePath) : "";
            string newContent = oldContent;

            try
            {
                using (var client = new WebClient())
                {
                    newContent = await client.DownloadStringTaskAsync(LiveFeedUrl).ConfigureAwait(false);

                    // Bail as early as possible to minimize package init time
                    if (newContent == oldContent)
                        return false;

                    // Test if reponse is a valid JSON object
                    var json = JObject.Parse(newContent);

                    if (json != null)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(LocalCachePath));
                        File.WriteAllText(LocalCachePath, newContent);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.Write(ex);
                return false;
            }

            return oldContent != newContent;
        }
    }
}