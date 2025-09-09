using System;

namespace musicStudioUnit.Configuration
{
    // Configuration Event Arguments
    public class XmlConfigLoadedEventArgs : EventArgs
    {
        public object LocalConfig { get; set; }
        public XmlConfigLoadedEventArgs(object localConfig) 
        {
            LocalConfig = localConfig;
        }
    }

    public class XmlConfigErrorEventArgs : EventArgs
    {
        public string ErrorMessage { get; set; }
        public Exception Exception { get; set; }
        public XmlConfigErrorEventArgs(string errorMessage, Exception ex = null)
        {
            ErrorMessage = errorMessage;
            Exception = ex;
        }
    }

    public class HttpConfigLoadedEventArgs : EventArgs
    {
        public object RemoteConfig { get; set; }
        public HttpConfigLoadedEventArgs(object remoteConfig)
        {
            RemoteConfig = remoteConfig;
        }
    }

    public class HttpConfigErrorEventArgs : EventArgs
    {
        public string ErrorMessage { get; set; }
        public Exception Exception { get; set; }
        public HttpConfigErrorEventArgs(string errorMessage, Exception ex = null)
        {
            ErrorMessage = errorMessage;
            Exception = ex;
        }
    }

    public class ConfigurationErrorEventArgs : EventArgs
    {
        public string ErrorMessage { get; set; }
        public Exception Exception { get; set; }
        public ConfigurationErrorEventArgs(string errorMessage, Exception ex = null)
        {
            ErrorMessage = errorMessage;
            Exception = ex;
        }
    }
}
