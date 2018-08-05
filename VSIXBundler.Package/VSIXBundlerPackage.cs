using log4net;
using log4net.Config;

using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

using VSIXBundler.Core;
using VSIXBundler.Core.Installer;
using VSIXBundler.Core.Logging;

namespace VSIXBundler.Package
{
    internal class LoggerFactory
    {
        public ILogger Create(ISettings settings)
        {
            return new Logger(settings);
        }
    }

    internal class SettingsFactory
    {
        public string VSIXName = "Bundler";

        public Uri LiveFeedUrl = new Uri("https://raw.githubusercontent.com/FireflyMigration/MultiProject/master/extensions.json");
        //public Uri LiveFeedUrl = new Uri("file://D://localGithub//MultiProject//extensions.json");

        public string RegistrySubKey => VSIXName;

        public ISettings Create()
        {
            var rp = new ResourceProviderFactory().Create();
            return new Settings(VSIXName, LiveFeedUrl.ToString(), RegistrySubKey, rp);
        }
    }

    [Guid(_packageGuid)]
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.ShellInitialized_string, PackageAutoLoadFlags.BackgroundLoad)]
    public sealed class InstallerPackage : AsyncPackage
    {
        public const string _packageGuid = "7D980AC4-E12E-48E4-9E7A-9EFD59B32AA9";
        private ILog _log = null;

        public InstallerPackage()
        {

        }
        protected override async System.Threading.Tasks.Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            initLogging();
            _log.Debug("STARTUP");
            var settings = new SettingsFactory().Create();
            InstallerService.Initialize(this, settings, getLogger(settings));
            await InstallerService.RunAsync().ConfigureAwait(false);
        }

        private void initLogging()
        {
            var loggingFilePath = findLoggingConfig();
            XmlConfigurator.ConfigureAndWatch(loggingFilePath);

            _log = LogManager.GetLogger(typeof(InstallerPackage));
        }

        private string GetVSIXFolder()
        {
            var codebase = typeof(InstallerPackage).Assembly.CodeBase;
            var uri = new Uri(codebase, UriKind.Absolute);
            return Path.GetDirectoryName(uri.LocalPath);
        }
        private FileInfo findLoggingConfig()
        {
            var folders = new[]
            {
                GetVSIXFolder(),
                Environment.ExpandEnvironmentVariables(@"%AppData%\bundler")
            };

            foreach (var f in folders)
            {
                var fi = new FileInfo(Path.Combine(f, "log4net.config"));
                if (fi.Exists) return fi;
            }

            return null;
        }

        private ILogger getLogger(ISettings settings)
        {
            return new LoggerFactory().Create(settings);
        }
    }
}