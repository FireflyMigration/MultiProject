using log4net;

using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.ExtensionManager;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.TaskStatusCenter;

using System;

using VSIXBundler.Core.Helpers;

using ILogger = VSIXBundler.Core.Logging.ILogger;
using Tasks = System.Threading.Tasks;

namespace VSIXBundler.Core.Installer
{
    public class InstallerService
    {
        private static ITaskHandler _handler;
        private static bool _hasShownProgress;
        private static AsyncPackage _package;
        private static ISettings _settings;
        private static ILogger _logger;
        private static ILog _log = LogManager.GetLogger(typeof(InstallerService));

        public static Installer Installer
        {
            get; private set;
        }

        public static void Initialize(AsyncPackage package, ISettings settings, ILogger logger)
        {
            _log.Debug("Initialise");
            _package = package;
            _settings = settings;
            _logger = logger;
            var registry = new RegistryKeyWrapper(package.UserRegistryRoot);
            var store = new DataStore(registry, settings);
            var feed = new LiveFeed(settings.LiveFeedUrl, settings.LiveFeedCachePath, _logger);

            Installer = new Installer(feed, store, _settings, _logger);
            Installer.Update += OnUpdate;
            Installer.Done += OnInstallationDone;

#if DEBUG
            // This deletes feed.json and installer.log so it finds updates
            Reset();
#endif

            _log.Debug("Init completed");
        }

        public static async Tasks.Task ResetAsync()
        {
            Reset();
            await RunAsync().ConfigureAwait(false);
        }

        private static void Reset()
        {
            Installer.Store.Reset();
            Installer.LiveFeed.Reset();
        }

        public static async Tasks.Task RunAsync()
        {
            _log.Debug("RunAsync");
            bool hasUpdates = await Installer.CheckForUpdatesAsync();

            if (!hasUpdates)
            {
                _log.Debug("Noupdates. Exiting");
                return;
            }

            _hasShownProgress = false;

            _log.Debug("Getting services");

            // Waits for MEF to initialize before the extension manager is ready to use
            await _package.GetServiceAsync(typeof(SComponentModel));

            var repository = await _package.GetServiceAsync(typeof(SVsExtensionRepository)) as IVsExtensionRepository;
            var manager = await _package.GetServiceAsync(typeof(SVsExtensionManager)) as IVsExtensionManager;
            _log.Debug("services complete");

            Version vsVersion = VsHelpers.GetVisualStudioVersion();

            _log.Debug("Setuptaskstatuscenter");
            _handler = await SetupTaskStatusCenter();
            _log.Debug("Setuptaskstatuscenter complete");

            _log.Debug("running installer async");
            var task = Installer.RunAsync(vsVersion, repository, manager, _handler.UserCancellation);
            _handler.RegisterTask(task);
            await task;

            _log.Debug("RunAsync complete");
        }

        private static async Tasks.Task<ITaskHandler> SetupTaskStatusCenter()
        {
            var tsc = await _package.GetServiceAsync(typeof(SVsTaskStatusCenterService)) as IVsTaskStatusCenterService;

            var options = default(TaskHandlerOptions);
            options.Title = _settings.VsixName;
            options.DisplayTaskDetails = task => { _logger.ShowOutputWindowPane(); };
            options.ActionsAfterCompletion = CompletionActions.None;

            var data = default(TaskProgressData);
            data.CanBeCanceled = true;

            return tsc.PreRegister(options, data);
        }

        private static void OnUpdate(object sender, Progress progress)
        {
            var data = new TaskProgressData
            {
                ProgressText = progress.Text,
                PercentComplete = progress.Percent,
                CanBeCanceled = true
            };

            _handler.Progress.Report(data);

            if (!_hasShownProgress)
            {
                _hasShownProgress = true;
                VsHelpers.ShowTaskStatusCenter();
            }
        }

        private static void OnInstallationDone(object sender, InstallResult results)
        {
            if (!_handler.UserCancellation.IsCancellationRequested && results.MustRestart)
            {
                VsHelpers.PromptForRestart(_settings);
            }
        }
    }
}