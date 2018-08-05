using log4net;

using Microsoft.VisualStudio.ExtensionManager;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using VSIXBundler.Core.Helpers;

using ILogger = VSIXBundler.Core.Logging.ILogger;

namespace VSIXBundler.Core.Installer
{
    /*
     * Copyright MadsKristensen https://github.com/madskristensen/WebEssentials2017
     */

    public class Installer
    {
        private readonly ISettings _settings;
        private readonly ILogger _logger;
        private Progress _progress;
        private ILog _log = LogManager.GetLogger(typeof(Installer));

        public Installer(LiveFeed feed, DataStore store, ISettings settings, ILogger logger)
        {
            _settings = settings;
            _logger = logger;
            LiveFeed = feed;
            Store = store;
        }

        public DataStore Store { get; }

        public LiveFeed LiveFeed { get; }

        public async Task<bool> CheckForUpdatesAsync()
        {
            var file = new FileInfo(LiveFeed.LocalCachePath);
            bool hasUpdates = false;

            hasUpdates = await LiveFeed.UpdateAsync().ConfigureAwait(false);

            return hasUpdates;
        }

        public async Task RunAsync(Version vsVersion, IVsExtensionRepository repository, IVsExtensionManager manager, CancellationToken cancellationToken)
        {
            _log.Debug("RunAsync started");
            IEnumerable<ExtensionEntry> toUninstall = GetExtensionsMarkedForDeletion(vsVersion);
            IEnumerable<ExtensionEntry> toInstall = GetMissingExtensions(manager).Except(toUninstall);

            int actions = toUninstall.Count() + toInstall.Count();
            if (actions > 0)
            {
                _progress = new Progress(actions);

                await UninstallAsync(toUninstall, repository, manager, cancellationToken).ConfigureAwait(false);
                var installationResult = await InstallAsync(toInstall, repository, manager, cancellationToken).ConfigureAwait(false);

                _log.Debug("Installation Completed ");
                _logger.Log(Environment.NewLine + _settings.ResourceProvider.InstallationComplete + Environment.NewLine);
                Done?.Invoke(this, installationResult);
            }
            else
            {
                _log.Debug("No actions to do");
            }
            _log.Debug("RunAsync ended");
        }


        private async Task<InstallResult> InstallAsync(IEnumerable<ExtensionEntry> extensions, IVsExtensionRepository repository, IVsExtensionManager manager, CancellationToken token)
        {
            _log.Debug("InstallAsync started");
            if (!extensions.Any() || token.IsCancellationRequested)
                return InstallResult.NothingToDo;

            return await Task.Factory.StartNew<InstallResult>(() =>
            {
                var ret = new InstallResult();
                try
                {
                    foreach (ExtensionEntry extension in extensions)
                    {
                        if (token.IsCancellationRequested)
                            return ret;

                        var result = InstallExtension(extension, repository, manager);
                        ret.AddResult(extension, result);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Log("Error installing " + ex.Message);
                }
                finally
                {
                    Store.Save();
                }

                return ret;
            }, token).ConfigureAwait(false);
        }

        private async Task UninstallAsync(IEnumerable<ExtensionEntry> extensions, IVsExtensionRepository repository, IVsExtensionManager manager, CancellationToken token)
        {
            if (!extensions.Any() || token.IsCancellationRequested)
                return;

            await Task.Run(() =>
            {
                try
                {
                    foreach (ExtensionEntry extension in extensions)
                    {
                        if (token.IsCancellationRequested)
                            return;

                        string msg = string.Format(_settings.ResourceProvider.UninstallingExtension, extension.Name);

                        OnUpdate(msg);
                        _logger.Log(msg, false);

                        try
                        {
                            if (manager.TryGetInstalledExtension(extension.Id, out IInstalledExtension installedExtension))
                            {
#if !DEBUG
                                manager.Uninstall(installedExtension);
                                Telemetry.Uninstall(extension.Id, true);
#endif

                                Store.MarkUninstalled(extension);
                                _logger.Log(_settings.ResourceProvider.Ok);
                            }
                        }
                        catch (Exception)
                        {
                            _logger.Log(_settings.ResourceProvider.Failed);
                            Telemetry.Uninstall(extension.Id, false);
                        }
                    }
                }
                finally
                {
                    Store.Save();
                }
            });
        }

        //Checks the version of the extension 
        private bool NewerVersionExists(IInstalledExtension extension, IVsExtensionRepository repository, GalleryEntry entry)
        {
            var version = extension.Header.Version;
            var strNewVersion = repository.GetCurrentExtensionVersions("ExtensionManagerQuery", new List<string>() { extension.Header.Identifier }, 1033).Single();
            var newVersion = Version.Parse(strNewVersion);

            if (newVersion > version)
            {
                return true;
            }

            return false;
        }

        private RestartReason InstallExtension(ExtensionEntry extension, IVsExtensionRepository repository, IVsExtensionManager manager)
        {
            GalleryEntry entry = null;
            OnUpdate(string.Format("{0} ({1})", _settings.ResourceProvider.InstallingExtension, extension.Name));
            var ret = RestartReason.None;

            try
            {
                _logger.Log($"{Environment.NewLine}{extension.Name}");
                _logger.Log("  " + _settings.ResourceProvider.Verifying, false);

                entry = repository.GetVSGalleryExtensions<GalleryEntry>(new List<string> { extension.Id }, 1033, false)?.FirstOrDefault();

                if (entry != null)
                {
                    _logger.Log(_settings.ResourceProvider.Ok); // Marketplace ok
#if DEBUG || true

                    var extensionsByAuthor = manager.GetInstalledExtensions().GroupBy(x => x.Header.Author).Select(y => new { y.Key, items = y }).ToArray();
#endif
                    var installed = manager.GetInstalledExtensions().SingleOrDefault(n => n.Header.Identifier == extension.Id);
                    _logger.Log("  " + _settings.ResourceProvider.Verifying, false);
                    IInstallableExtension installable = null;

                    if (installed != null)
                    {
                        if (NewerVersionExists(installed, repository, entry))
                        {
                            installed = null;
                        }
                    }
                    _logger.Log("  " + _settings.ResourceProvider.Ok);
                    if (installed == null)
                    {
                        _logger.Log("  " + _settings.ResourceProvider.Downloading, false);
                        installable = repository.Download(entry);
                        _logger.Log(_settings.ResourceProvider.Ok); // Download ok
                    }

                    if (installable == null)
                    {
                        _logger.Log(" nothing to do");
                    }
                    else
                    {

                        _logger.Log("  " + _settings.ResourceProvider.Installing, false);

                        ret = manager.Install(installable, false);
                        _logger.Log(_settings.ResourceProvider.Ok); // Install ok
                    }

                    Telemetry.Install(extension.Id, true);
                }
                else
                {
                    _logger.Log(_settings.ResourceProvider.Failed); // Markedplace failed
                }
            }
            catch (Exception e)
            {
                _logger.Log(_settings.ResourceProvider.Failed);
                _logger.Log("Failed to install package: " + e.Message);
                _log.Error(e);
                Telemetry.Install(extension.Id, false);
            }
            finally
            {
                if (entry != null)
                {
                    Store.MarkInstalled(extension);
                }
            }

            return ret;
        }

        private IEnumerable<ExtensionEntry> GetMissingExtensions(IVsExtensionManager manager)
        {
            IEnumerable<IInstalledExtension> installed = (IEnumerable<IInstalledExtension>)manager.GetInstalledExtensions();
            IEnumerable<ExtensionEntry> notInstalled = LiveFeed.Extensions.Where(ext => !installed.Any(ins => ins.Header.Identifier == ext.Id));

            return notInstalled;
        }

        public IEnumerable<ExtensionEntry> GetExtensionsMarkedForDeletion(Version VsVersion)
        {
            return LiveFeed.Extensions.Where(ext => ext.MinVersion > VsVersion || ext.MaxVersion < VsVersion);
        }

        private void OnUpdate(string text)
        {
            _progress.Current += 1;
            _progress.Text = text;

            Update?.Invoke(this, _progress);
        }

        public event EventHandler<Progress> Update;

        public event EventHandler<InstallResult> Done;
    }

    public class InstallResult
    {
        private List<AResult> _results = new List<AResult>();

        public static InstallResult NothingToDo
        {
            get { return new InstallResult(); }
        }

        private class AResult
        {
            public ExtensionEntry Extension { get; }
            public RestartReason Result { get; }

            public AResult(ExtensionEntry extension, RestartReason result)
            {
                this.Extension = extension;
                this.Result = result;
            }
        }

        public bool Any
        {
            get { return _results.Any(); }
        }

        public bool MustRestart
        {
            get { return _results.Any(x => x.Result != RestartReason.None); }
        }

        public void AddResult(ExtensionEntry extension, RestartReason result)
        {
            _results.Add(new AResult(extension, result));
        }
    }
}