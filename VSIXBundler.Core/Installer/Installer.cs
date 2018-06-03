using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.VisualStudio.ExtensionManager;

using VSIXBundler.Core.Helpers;

using ILogger = VSIXBundler.Core.Helpers.ILogger;

namespace VSIXBundler.Core.Installer
{
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
                await InstallAsync(toInstall, repository, manager, cancellationToken).ConfigureAwait(false);

                _logger.Log(Environment.NewLine + _settings.ResourceProvider.InstallationComplete + Environment.NewLine);
                Done?.Invoke(this, actions);
            }
        }

        private async Task InstallAsync(IEnumerable<ExtensionEntry> extensions, IVsExtensionRepository repository, IVsExtensionManager manager, CancellationToken token)
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

                        InstallExtension(extension, repository, manager);
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
            }).ConfigureAwait(false);
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

        private void InstallExtension(ExtensionEntry extension, IVsExtensionRepository repository, IVsExtensionManager manager)
        {
            GalleryEntry entry = null;
            OnUpdate(string.Format(_settings.ResourceProvider.InstallingExtension, extension.Name));

            try
            {
                _logger.Log($"{Environment.NewLine}{extension.Name}");
                _logger.Log("  " + _settings.ResourceProvider.Verifying, false);

                entry = repository.GetVSGalleryExtensions<GalleryEntry>(new List<string> { extension.Id }, 1033, false)?.FirstOrDefault();

                if (entry != null)
                {
                    _logger.Log(_settings.ResourceProvider.Ok); // Marketplace ok
                    _logger.Log("  " + _settings.ResourceProvider.Downloading, false);
#if !DEBUG
                    IInstallableExtension installable = repository.Download(entry);
#endif
                    _logger.Log(_settings.ResourceProvider.Ok); // Download ok
                    _logger.Log("  " + _settings.ResourceProvider.Installing, false);
#if !DEBUG
                    manager.Install(installable, false);
#else
                    Thread.Sleep(2000);
#endif
                    _logger.Log(_settings.ResourceProvider.Ok); // Install ok
                    Telemetry.Install(extension.Id, true);
                }
                else
                {
                    _logger.Log(_settings.ResourceProvider.Failed); // Markedplace failed
                }
            }
            catch (Exception)
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

        public event EventHandler<int> Done;
    }
}