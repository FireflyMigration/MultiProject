using log4net;
using log4net.Appender;
using log4net.Core;

using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

using System;
using System.IO;

using VSIXBundler.Core.Helpers;

namespace VSIXBundler.Core.Logging
{
    public interface ILogger
    {
        void Log(string message, bool addNewLine = true);

        void ShowOutputWindowPane();
    }

    public class OutputWindowLogAppender : IAppender
    {
        private static IVsOutputWindowPane _pane;
        private static IVsOutputWindow _output = (IVsOutputWindow)ServiceProvider.GlobalProvider.GetService(typeof(SVsOutputWindow));

        public OutputWindowLogAppender()
        {

        }

        public void Close()
        {
            // nothing to do
        }

        public void DoAppend(LoggingEvent loggingEvent)
        {
            ThreadHelper.Generic.BeginInvoke(() =>
            {
                try
                {
                    var appName = "Bundler";
                    if (EnsurePane(appName))
                    {
                        using (var sw = new StringWriter())
                        {
                            loggingEvent.WriteRenderedMessage(sw);

                            _pane.OutputStringThreadSafe(sw.ToString());
                            _pane.OutputStringThreadSafe(Environment.NewLine);
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.Write(ex);
                }
            });
        }
        private bool EnsurePane(string name)
        {
            if (_pane == null)
            {
                var guid = Guid.NewGuid();
                _output.CreatePane(ref guid, name, 1, 1);
                _output.GetPane(ref guid, out _pane);
            }

            return _pane != null;
        }
        public string Name { get; set; }
    }
    public class Logger : ILogger
    {
        private readonly ISettings _settings;
        private static IVsOutputWindowPane _pane;
        private static IVsOutputWindow _output = (IVsOutputWindow)ServiceProvider.GlobalProvider.GetService(typeof(SVsOutputWindow));
        private ILog _log = LogManager.GetLogger(typeof(Logger));
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

                    _log.Debug(message);

                }
                catch (Exception ex)
                {
                    _log.Error(message);
                }
            });
        }

        public void ShowOutputWindowPane()
        {
            ThreadHelper.Generic.BeginInvoke(() =>
            {

                VsHelpers.ShowOutputWindow();

            });
        }


    }
}