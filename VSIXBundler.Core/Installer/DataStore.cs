using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

using Newtonsoft.Json;

namespace VSIXBundler.Core.Installer
{
    public interface IResourceProvider
    {
        string Installed { get; set; }
        string Uninstalled { get; set; }
        string InstallationComplete { get; }
        string UninstallingExtension { get; }
        string Ok { get; }
        string Failed { get; }
        string InstallingExtension { get; }
        string Verifying { get; }
        string Downloading { get; }
        string Installing { get; }
    }

    public interface IResourceProviderFactory
    {
        IResourceProvider Create();
    }

    public class ResourceProviderFactory : IResourceProviderFactory
    {
        private static ResourceProviderFactory _instance;

        public static IResourceProviderFactory Instance
        {
            get
            {
                if (_instance == null) _instance = new ResourceProviderFactory();

                return _instance;
            }
        }

        public IResourceProvider Create()
        {
            //todo: Implement resourceprovider
            return new ResourceProvider();
        }

        private class ResourceProvider : IResourceProvider
        {
            public string Installed { get; set; }
            public string Uninstalled { get; set; }
            public string InstallationComplete { get; set; }
            public string UninstallingExtension { get; set; }
            public string Ok { get; set; }
            public string Failed { get; set; }
            public string InstallingExtension { get; set; }
            public string Verifying { get; set; }
            public string Downloading { get; set; }
            public string Installing { get; set; }

            public ResourceProvider()
            {
                // initialise strings from their property names
                foreach (var p in this.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
                {
                    p.SetValue(this, p.Name);
                }
            }
        }
    }

    public class DataStore
    {
        private string _installed;
        private string _uninstalled;

        private static string _logFile;
        private readonly ISettings _settings;
        private IRegistryKey _key;

        public DataStore(IRegistryKey key, ISettings settings)
        {
            var filePath = settings.LogFilePath;
            _logFile = filePath;
            _settings = settings;
            _key = key;

            InitializeResources();
            Initialize();
        }

        private void InitializeResources()
        {
            var res = _settings.ResourceProvider;
            _installed = res.Installed;
            _uninstalled = res.Uninstalled;
        }

        public List<LogMessage> Log { get; private set; } = new List<LogMessage>();

        public void MarkInstalled(ExtensionEntry extension)
        {
            Log.Add(new LogMessage(extension, _installed));
        }

        public void MarkUninstalled(ExtensionEntry extension)
        {
            Log.Add(new LogMessage(extension, _uninstalled));
        }

        public bool HasBeenInstalled(string id)
        {
            return Log.Any(ext => ext.Id == id && ext.Action == _installed);
        }

        public void Save()
        {
            string json = JsonConvert.SerializeObject(Log);
            File.WriteAllText(_logFile, json);

            UpdateRegistry();
        }

        public bool Reset()
        {
            try
            {
                File.Delete(_logFile);
                Log.Clear();
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.Write(ex);
                return false;
            }
        }

        private void Initialize()
        {
            try
            {
                if (File.Exists(_logFile))
                {
                    Log = JsonConvert.DeserializeObject<List<LogMessage>>(File.ReadAllText(_logFile));
                    UpdateRegistry();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.Write(ex);
                File.Delete(_logFile);
            }
        }

        private void UpdateRegistry()
        {
            string uninstall = string.Join(";", Log.Where(l => l.Action == _uninstalled).Select(l => l.Id));

            using (_key.CreateSubKey(_settings.RegistrySubKey))
            {
                _key.SetValue("disable", uninstall);
            }
        }

        public struct LogMessage
        {
            public LogMessage(ExtensionEntry entry, string action)
            {
                Id = entry.Id;
                Name = entry.Name;
                Action = action;
                Date = DateTime.Now;
            }

            public string Id { get; set; }
            public string Name { get; set; }
            public DateTime Date { get; set; }
            public string Action { get; set; }

            public override string ToString()
            {
                return $"{Date.ToString("yyyy-MM-dd")} {Action} {Name}";
            }
        }
    }
}