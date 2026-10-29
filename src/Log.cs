using System;
using System.IO;

namespace CPRB.Bannerlord
{
    // Plain text log next to the settings file:
    // Documents\Mount and Blade II Bannerlord\Configs\ColoredPoliticalRegionsBorders.log
    internal static class Log
    {
        private static readonly object Gate = new object();
        private static string _path;

        public static string Path => _path;

        public static void Start()
        {
            try
            {
                _path = System.IO.Path.Combine(Settings.ConfigDirectory, "ColoredPoliticalRegionsBorders.log");
                Directory.CreateDirectory(Settings.ConfigDirectory);
                File.WriteAllText(_path, "Colored Political Regions & Borders " + SubModule.Version + "  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine);
            }
            catch
            {
                _path = null;
            }
        }

        public static void Info(string message)
        {
            if (_path == null)
                return;
            lock (Gate)
            {
                try
                {
                    File.AppendAllText(_path, DateTime.Now.ToString("HH:mm:ss.fff") + "  " + message + Environment.NewLine);
                }
                catch
                {
                }
            }
        }

        public static void Error(string where, Exception e)
        {
            Info("ERROR in " + where + ": " + e);
        }
    }
}
