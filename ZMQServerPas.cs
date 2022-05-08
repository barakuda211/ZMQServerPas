using System;
using System.IO;
using System.Text;
using NetMQ.Sockets;
using NetMQ;
using PascalABCCompiler.SyntaxTree;
using PascalABCCompiler.Errors;
using PascalABCCompiler;
using System.Threading;

namespace ZMQServerPas
{
    class ZMQServerPas
    {
        public static ResponseSocket server;
        public static PushSocket output;
        public static PullSocket input;
        public static PullSocket heartbeat;
        public static Thread inputLoop = null;
        public static Thread heartbeatLoop = null;
        public static StreamWriter currentInputStream = null;

        public delegate void InputHandler(string output);
        public static event InputHandler InputReceived;

        static string Compile(Compiler c, string myfilename)
        {
                var co = new CompilerOptions(myfilename, CompilerOptions.OutputType.ConsoleApplicaton);
                co.UseDllForSystemUnits = true;
                co.Debug = false;
                co.ForDebugging = false;
                c.Reload();
                return c.Compile(co);
        }
        static string RunProcess(string myexefilename, PushSocket output)
        {

            string exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string exeDir = System.IO.Path.GetDirectoryName(exe);

            var outputstring = new StringBuilder();
            var pabcnetcProcess = new System.Diagnostics.Process();
            pabcnetcProcess.StartInfo.FileName = myexefilename;
            pabcnetcProcess.StartInfo.WorkingDirectory = exeDir+"\\temp\\";
            pabcnetcProcess.StartInfo.UseShellExecute = false;
            pabcnetcProcess.StartInfo.StandardOutputEncoding = Encoding.UTF8;
            pabcnetcProcess.EnableRaisingEvents = true;

            pabcnetcProcess.StartInfo.RedirectStandardOutput = true;
            pabcnetcProcess.StartInfo.RedirectStandardError = true;
            pabcnetcProcess.StartInfo.RedirectStandardInput = true;


            pabcnetcProcess.OutputDataReceived += (o, e) =>
            {
                if (e.Data != null)
                {
                    var dataBytes = Encoding.UTF8.GetBytes(e.Data);
                    var encodedBytes = Encoding.Convert(Encoding.UTF8, Encoding.Default, dataBytes);
                    var encodedData = Encoding.Default.GetString(encodedBytes);
                    encodedData = encodedData.Replace("[FAKELINE]", "").Replace("[NEWLINE]", "</br>");
                    output.SendFrame(encodedData);
                }
            };

            pabcnetcProcess.ErrorDataReceived += (o, e) =>
            {
                if (e.Data != null)
                {
                    if (e.Data == "[READLNSIGNAL]")
                    {
                        output.SendFrame("[READLNSIGNAL]");
                    }
                    else if (e.Data == "[CODEPAGE65001]")
                        return;
                    else
                    {
                        var dataBytes = Encoding.UTF8.GetBytes(e.Data);
                        var encodedBytes = Encoding.Convert(Encoding.UTF8, Encoding.Default, dataBytes);
                        var encodedData = Encoding.Default.GetString(encodedBytes);
                        encodedData = encodedData.Replace("[FAKELINE]", "").Replace("[NEWLINE]", "</br>");
                        Console.WriteLine(encodedData);
                        output.SendFrame(encodedData);
                    }
                        
                }
            };

            pabcnetcProcess.Start();

            currentInputStream = new StreamWriter(pabcnetcProcess.StandardInput.BaseStream, Encoding.GetEncoding("cp866"));
            currentInputStream.AutoFlush = true;
            pabcnetcProcess.BeginOutputReadLine();
            pabcnetcProcess.BeginErrorReadLine();

            pabcnetcProcess.WaitForExit();
            //pabcnetcProcess.WaitForExit(5000);
            if (!pabcnetcProcess.HasExited)
            { // убить процесс если он работвет больше 5 секунд. Скорее всего он завис
                pabcnetcProcess.Kill();
                outputstring.AppendLine("Программа завершена. Она работала более 5 секунд и, вероятно, зависла");
            }
            return outputstring.ToString();
        }
        static void Main(string[] args)
        {
            //if (args.Length < 4)
            //{
            //    Console.WriteLine("No arguments!");
            //    Console.ReadKey();
            //    return;
            //}
            Console.WriteLine("Server start");
            server = new ResponseSocket();
            output = new PushSocket();
            input = new PullSocket();
            heartbeat = new PullSocket();
            heartbeat.Connect("tcp://127.0.0.1:5554");
            input.Connect("tcp://127.0.0.1:5555");
            server.Connect("tcp://127.0.0.1:5557");
            output.Connect("tcp://127.0.0.1:5556");
            StartLoop();

            StringResourcesLanguage.LoadDefaultConfig();
            var c = new Compiler();

            while (true)
            {
                var code = server.ReceiveFrameString();
                string myfilename, myexefilename;
                try
                {
                    myfilename = Helper.CreateTempPas(code);
                    myexefilename = Compile(c, myfilename);

                    if (myexefilename == null)
                    {
                        var msg = "";
                        if (c.ErrorsList.Count > 0)
                        {
                            var err = c.ErrorsList[0];
                            msg = Helper.EnhanceErrorMsg(err) + '\n';
                        }
                        server.SendFrame(msg);
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    server.SendFrame("Сломался компилятор(((( "+ex.Message);
                    continue;
                }

                server.SendFrame("[OK]");
                myexefilename = myexefilename.Replace(".pas", ".exe");
                RunProcess(myexefilename, output);

                if (File.Exists(myfilename))
                    File.Delete(myfilename);
                if (File.Exists(myexefilename))
                    File.Delete(myexefilename);
                output.SendFrame("[END]");

                //server.SendFrame(output);
            }

            //readln;
            server.Dispose();
        }

        public static void TempInput(string s)
        {
            currentInputStream.WriteLine(s);
        }

        public static void StartLoop()
        {
            inputLoop = new Thread(InputLoop);
            inputLoop.Start();

            heartbeatLoop = new Thread(HeartBeatLoop);
            heartbeatLoop.Start();

            InputReceived += TempInput;
        }

        private static void HeartBeatLoop()
        {
            while (true)
            {
                Thread.Sleep(1000);
                if (heartbeat.HasIn && heartbeat.ReceiveFrameString() == "[ALIVE]")
                    continue;

                Environment.Exit(0);
            }
        }

        private static void InputLoop()
        {
            while (true)
            {
                var output = input.ReceiveFrameString();
                InputReceived?.Invoke(output);
            }
        }


    }
}
