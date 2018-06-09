using Microsoft.VisualStudio.ExtensionManager;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using VSIXBundler.Core.Helpers;

using ILogger = VSIXBundler.Core.Helpers.ILogger;

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

            if (!file.Exists || file.LastWriteTime < DateTime.Now.AddDays(-_settings.UpdateIntervalDays))
            {
                hasUpdates = await LiveFeed.UpdateAsync().ConfigureAwait(false);
            }
            else
            {
                await LiveFeed.ParseAsync().ConfigureAwait(false);
            }

            return hasUpdates;
        }

        public async Task RunAsync(Version vsVersion, IVsExtensionRepository repository, IVsExtensionManager manager, CancellationToken cancellationToken)
        {
            IEnumerable<ExtensionEntry> toUninstall = GetExtensionsMarkedForDeletion(vsVersion);
            IEnumerable<ExtensionEntry> toInstall = GetMissingExtensions(manager).Except(toUninstall);

            int actions = toUninstall.Count() + toInstall.Count();
            if (actions > 0)
            {
                _progress = new Progress(actions);

                await UninstallAsync(toUninstall, repository, manager, cancellationToken).ConfigureAwait(false);
                var installationResult = await InstallAsync(toInstall, repository, manager, cancellationToken).ConfigureAwait(false);

                _logger.Log(Environment.NewLine + _settings.ResourceProvider.InstallationComplete + Environment.NewLine);
                Done?.Invoke(this, installationResult);
            }
        }

        private async Task<InstallResult> InstallAsync(IEnumerable<ExtensionEntry> extensions, IVsExtensionRepository repository, IVsExtensionManager manager, CancellationToken token)
        {
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
                    System.Diagnostics.Debug.Write(ex);
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

        //Checks the version of the extension on the VS Gallery and downloads it if necessary.
        private IInstallableExtension FetchIfUpdated(IInstalledExtension extension, IVsExtensionRepository repository, GalleryEntry entry)
        {
            var version = extension.Header.Version;
            var strNewVersion = repository.GetCurrentExtensionVersions("ExtensionManagerQuery", new List<string>() { extension.Header.Identifier }, 1033).Single();
            var newVersion = Version.Parse(strNewVersion);

            if (newVersion > version)
            {
                var newestVersion = repository.Download(entry);
                return newestVersion;
            }

            return null;
        }

        private RestartReason InstallExtension(ExtensionEntry extension, IVsExtensionRepository repository, IVsExtensionManager manager)
        {
            GalleryEntry entry = null;
            OnUpdate(string.Format("{0} ({1})", _settings.ResourceProvider.InstallingExtension, extension.Name));

            try
            {
                _logger.Log($"{Environment.NewLine}{extension.Name}");
                _logger.Log("  " + _settings.ResourceProvider.Verifying, false);

                entry = repository.GetVSGalleryExtensions<GalleryEntry>(new List<string> { extension.Id }, 1033, false)?.FirstOrDefault();

                if (entry != null)
                {
                    _logger.Log(_settings.ResourceProvider.Ok); // Marketplace ok

                    var installed = manager.GetInstalledExtensions().Where(n => n.Header.Identifier == extension.Id).SingleOrDefault();
                    _logger.Log("  " + _settings.ResourceProvider.Downloading, false);
                    IInstallableExtension installable = installed == null ? repository.Download(entry) : FetchIfUpdated(installed, repository, entry);

                    if (installable == null)
                    {
                        _logger.Log(" nothing to do");
                    }
                    else
                    {
#if !DEBUG || true

#endif
                        _logger.Log(_settings.ResourceProvider.Ok); // Download ok
                        _logger.Log("  " + _settings.ResourceProvider.Installing, false);
#if !DEBUG || true

                        return manager.Install(installable, false);
#else
                    Thread.Sleep(2000);
#endif
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
                Telemetry.Install(extension.Id, false);
            }
            finally
            {
                if (entry != null)
                {
                    Store.MarkInstalled(extension);
                }
            }

            return RestartReason.None;
        }

        private IEnumerable<ExtensionEntry> GetMissingExtensions(IVsExtensionManager manager)
        {
            IEnumerable<IInstalledExtension> installed = (IEnumerable<IInstalledExtension>)manager.GetInstalledExtensions();
            IEnumerable<ExtensionEntry> notInstalled = LiveFeed.Extensions.Where(ext => !installed.Any(ins => ins.Header.Identifier == ext.Id));

            return notInstalled.Where(ext => !Store.HasBeenInstalled(ext.Id));
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