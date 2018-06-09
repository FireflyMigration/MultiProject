using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;

using System;
using System.Runtime.InteropServices;
using System.Threading;

using VSIXBundler.Core;
using VSIXBundler.Core.Helpers;
using VSIXBundler.Core.Installer;

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

        //public Uri LiveFeedUrl = new Uri("https://raw.githubusercontent.com/stickleprojects/MultiProject/master/extensions.json");
        public Uri LiveFeedUrl = new Uri("file://D://localGithub//MultiProject//extensions.json");

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

        protected override async System.Threading.Tasks.Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            var settings = new SettingsFactory().Create();
            InstallerService.Initialize(this, settings, getLogger(settings));
            await InstallerService.RunAsync().ConfigureAwait(false);
        }

        private ILogger getLogger(ISettings settings)
        {
            return new LoggerFactory().Create(settings);
        }
    }
}