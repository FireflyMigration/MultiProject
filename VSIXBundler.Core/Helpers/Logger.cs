using System;

using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace VSIXBundler.Core.Helpers
{
    public interface ILogger
    {
        void Log(string message, bool addNewLine = true);

        void ShowOutputWindowPane();
    }

    public class Logger : ILogger
    {
        private readonly ISettings _settings;
        private static IVsOutputWindowPane _pane;
        private static IVsOutputWindow _output = (IVsOutputWindow)ServiceProvider.GlobalProvider.GetService(typeof(SVsOutputWindow));

        public Logger(ISettings settings)
        {
            _settings = settings;
        }

        public void Log(string message, bool addNewLine = true)
        {
            ThreadHelper.Generic.BeginInvoke(() =>
            {
                try
                {
                    if (EnsurePane())
                    {
                        if (addNewLine)
                        {
                            message += Environment.NewLine;
                        }

                        _pane.OutputString(message);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.Write(ex);
                }
            });
        }

        public void ShowOutputWindowPane()
        {
            ThreadHelper.Generic.BeginInvoke(() =>
            {
                if (EnsurePane())
                {
                    VsHelpers.ShowOutputWindow();
                    _pane.Activate();
                }
            });
        }

        private bool EnsurePane()
        {
            if (_pane == null)
            {
                var guid = Guid.NewGuid();
                _output.CreatePane(ref guid, _settings.VsixName, 1, 1);
                _output.GetPane(ref guid, out _pane);
            }

            return _pane != null;
        }
    }
}