using System;
using System.IO;
using System.Text;
using NetMQ.Sockets;
using NetMQ;
using PascalABCCompiler.SyntaxTree;
using PascalABCCompiler.Errors;
using PascalABCCompiler;
using System.Threading;
using System.Diagnostics;
using System.Globalization;

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

        public static Process pabcnetcProcess = null;

        public delegate void InputHandler(string output);
        public static event InputHandler InputReceived;

        public static StringBuilder resultString = new StringBuilder();

        static string Compile(Compiler c, string myfilename)
        {
            var co = new CompilerOptions(myfilename, CompilerOptions.OutputType.ConsoleApplicaton);
            co.UseDllForSystemUnits = false;
            co.Debug = false;
            co.ForDebugging = true;
            c.Reload();
            var res = c.Compile(co);
            c.Free();
            c.ClearAll(true);
            GC.Collect();
            return res;
        }
        static string RunProcess(string myexefilename, PushSocket output)
        {

            string exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string exeDir = System.IO.Path.GetDirectoryName(exe);

            var outputstring = new StringBuilder();
            pabcnetcProcess = new System.Diagnostics.Process();
            //pabcnetcProcess.StartInfo.Verb = "mono";
            pabcnetcProcess.StartInfo.FileName = myexefilename;
            pabcnetcProcess.StartInfo.CreateNoWindow = true;
            //output.SendFrame(exeDir);
            pabcnetcProcess.StartInfo.WorkingDirectory = exeDir + "/temp/";
            pabcnetcProcess.StartInfo.UseShellExecute = false;
            pabcnetcProcess.StartInfo.StandardOutputEncoding = Encoding.GetEncoding(1251);
            pabcnetcProcess.StartInfo.StandardErrorEncoding = Encoding.GetEncoding(1251);
            pabcnetcProcess.EnableRaisingEvents = true;

            pabcnetcProcess.StartInfo.RedirectStandardOutput = true;
            pabcnetcProcess.StartInfo.RedirectStandardError = true;
            pabcnetcProcess.StartInfo.RedirectStandardInput = true;


            pabcnetcProcess.OutputDataReceived += (o, e) =>
            {
                if (e.Data != null)
                {
                    var encodedData = e.Data;
                    encodedData = encodedData.Replace("[FAKELINE]", " ").Replace("[NEWLINE]", "</br>");
                    if (encodedData.Replace(" ", "") == "")
                        return;
                    resultString.Append(encodedData);
                    output.SendFrame(resultString.ToString());
                }
                //if (e.Data != null)
                //{
                //    var dataBytes = Encoding.UTF8.GetBytes(e.Data);
                //    var encodedBytes = Encoding.Convert(Encoding.UTF8, Encoding.Default, dataBytes);
                //    var encodedData = Encoding.Default.GetString(encodedBytes);
                //    encodedData = encodedData.Replace("[FAKELINE]", " ").Replace("[NEWLINE]", "</br>");
                //    if (encodedData.Replace(" ", "") == "")
                //        return;
                //    resultString.Append(encodedData);
                //    output.SendFrame(resultString.ToString());
                //}
            };

            var errorResult = "";
            pabcnetcProcess.ErrorDataReceived += (o, e) =>
            {
                if (e.Data != null)
                {
                    Logger.Log("Error: "+e.Data);
                    if (e.Data == "[READLNSIGNAL]")
                    {
                        Thread.Sleep(300);
                        output.SendFrame("[READLNSIGNAL]");
                    }
                    else if (e.Data == "[CODEPAGE65001]")
                        return;
                    else
                    {
                        if (e.Data == "")
                            return;

                        //Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;

                        //var dataBytes = Encoding.UTF8.GetBytes(e.Data);
                        //var encodedBytes = Encoding.Convert(Encoding.UTF8, Encoding.Default, dataBytes);
                        //var encodedData = Encoding.Default.GetString(encodedBytes);

                        //encodedData = encodedData.Replace("[FAKELINE]", "").Replace("[NEWLINE]", "</br>");
                        var encodedData = e.Data;
                        errorResult += encodedData + "<br />";
                        Console.WriteLine(encodedData);
                        output.SendFrame(errorResult);
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
            Logger.Init();
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            if (args.Length < 4)
            {
                Console.WriteLine("No arguments!");
                Console.ReadKey();
                return;
            }
            Console.WriteLine("Server start");
            server = new ResponseSocket();
            output = new PushSocket();
            input = new PullSocket();
            heartbeat = new PullSocket();
            server.Connect("tcp://127.0.0.1:"+args[0]);
            Console.WriteLine("Server socket started on " + args[0] + " port");
            output.Connect("tcp://127.0.0.1:" + args[1]);
            Console.WriteLine("Output socket started on " + args[1] + " port");
            input.Connect("tcp://127.0.0.1:" + args[2]);
            Console.WriteLine("Input socket started on " + args[2] + " port");
            heartbeat.Connect("tcp://127.0.0.1:" + args[3]);
            Console.WriteLine("HB socket started on " + args[3] + " port");
            StartLoop();

            Logger.Log("Loops started!");

            StringResourcesLanguage.LoadDefaultConfig();
            var c = new Compiler();

            Logger.Log("Compiler created, ready for work!");
            while (true)
            {
                var code = server.ReceiveFrameString();
                string filename="", myfilename="", myexefilename="";
                try
                {
                    myfilename = Helper.CreateTempPas(code);
                    filename = myfilename.Replace(".pas", "");
                    myexefilename = Compile(c, myfilename);

                    if (myexefilename == null)
                    {
                        var msg = "";
                        if (c.ErrorsList.Count > 0)
                        {
                            var err = c.ErrorsList[0];
                            msg = Helper.EnhanceErrorMsg(err) + '\n';
                            if (msg == "\n")
                                msg = err.ToString();
                        }
                        server.SendFrame(msg);
                        ClearTempFiles(filename);
                        GC.Collect();
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    server.SendFrame("Сломался компилятор: " + ex.Message);
                    ClearTempFiles(filename);
                    GC.Collect();
                    continue;
                }
                
                server.SendFrame("[OK]");
                myexefilename = myexefilename.Replace(".pas", ".exe");
                
                RunProcess(myexefilename, output);

                ClearTempFiles(filename);
                resultString.Clear();
                output.SendFrame("[END]");
                GC.Collect();
                //server.SendFrame(output);
            }

            //readln;
            server.Dispose();
        }

        public static void ClearTempFiles(string filename)
        {
            if (File.Exists(filename+".pas"))
                File.Delete(filename + ".pas");
            if (File.Exists(filename + ".exe"))
                File.Delete(filename + ".exe");
            if (File.Exists(filename + ".exe.mdb"))
                File.Delete(filename + ".exe.mdb");
        }

        public static void TempInput(string s)
        {
            if (s == "[BREAK]")
            {
                if (pabcnetcProcess != null && !pabcnetcProcess.HasExited)
                {
                    pabcnetcProcess.Kill();
                    return;
                }
                return;
            }
            if (!pabcnetcProcess.HasExited)
            {
                resultString.Append(s + "</br>");
                output.SendFrame(resultString.ToString());
                currentInputStream.WriteLine(s);
            }
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
                Logger.Log("Kernel is dead, exiting...");
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
