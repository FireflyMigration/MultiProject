using System;

namespace VSIXBundler.Core.Installer
{
    public interface IRegistryKey : IDisposable
    {
        IRegistryKey CreateSubKey(string subKey);

        void SetValue(string name, object value);

        object GetValue(string name);
    }
}