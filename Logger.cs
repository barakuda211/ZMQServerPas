using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

namespace ZMQServerPas
{
    public static class Logger
    {


        //private static string logPath = @"C:\Users\Tema-\Desktop\JupyterPascalABC.NET\Log\";
        //private static string logPath = @"C:\Users\barakuda\Desktop\jupyter\logs\";
        private static string logPath = null;

        public static void Init()
        {
            string exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string exeDir = System.IO.Path.GetDirectoryName(exe);
            if (logPath == null)
                logPath = exeDir + "/logs/";
            if (!Directory.Exists(logPath))
                Directory.CreateDirectory(logPath);
            Clear();
        }

        public static void Clear()
        {

        }

        public static void Log(string message, string filenameTo = "CompilerLog.txt")
        {
            try
            {
                string path = logPath + filenameTo;

                message = DateTime.Now + " " + message + "\n";

                File.AppendAllText(path, message);
                if (filenameTo != "CompilerLog.txt")
                    File.AppendAllText(logPath + "CompilerLog.txt", message);
            }
            catch (Exception e)
            {

            }
        }
    }
}
