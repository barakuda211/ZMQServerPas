using System;
using System.IO;
using System.Text;
using NetMQ.Sockets;
using NetMQ;
using PascalABCCompiler.SyntaxTree;
using PascalABCCompiler.Errors;
using PascalABCCompiler;

namespace ZMQServerPas
{
    class ZMQServerPas
    {
        static string Compile(Compiler c, string myfilename) {
            var co = new CompilerOptions(myfilename, CompilerOptions.OutputType.ConsoleApplicaton);
            co.UseDllForSystemUnits = true;
            co.Debug = false;
            co.ForDebugging = false;
            c.Reload();
            return c.Compile(co);
        }
        static string RunProcess(string myexefilename, PublisherSocket output) {

            string exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string exeDir = System.IO.Path.GetDirectoryName(exe);
            var exePath = exeDir + $"\\PABCCompiler\\temp\\"+ myexefilename;

            var outputstring = new StringBuilder();
            var pabcnetcProcess = new System.Diagnostics.Process();
            pabcnetcProcess.StartInfo.FileName = myexefilename;
            pabcnetcProcess.StartInfo.UseShellExecute = false;
            //pabcnetcProcess.StartInfo.CreateNoWindow:= true;
            pabcnetcProcess.StartInfo.RedirectStandardOutput = true;
            //pabcnetcProcess.StartInfo.RedirectStandardInput = true;
            //pabcnetcProcess.StartInfo.StandardOutputEncoding = System.Text.Encoding.UTF8;
            pabcnetcProcess.EnableRaisingEvents = true;

            pabcnetcProcess.StartInfo.RedirectStandardOutput = true;
            pabcnetcProcess.StartInfo.RedirectStandardError = true;
            pabcnetcProcess.StartInfo.RedirectStandardInput = true;
            pabcnetcProcess.StartInfo.StandardErrorEncoding = Encoding.Default;
            //pabcnetcProcess.StartInfo.StandardInputEncoding = Encoding.Default;
            pabcnetcProcess.StartInfo.StandardOutputEncoding = Encoding.Default;

            
            pabcnetcProcess.OutputDataReceived += (o, e) =>
            {
                if (e.Data != null)
                {
                    output.SendFrame(e.Data);
                }
            };

            pabcnetcProcess.ErrorDataReceived += (o, e) =>
            {
                if (e.Data != null)
                {
                    //TODO обработка ввода
                }
            };

            pabcnetcProcess.Start();
            pabcnetcProcess.BeginOutputReadLine();
            pabcnetcProcess.WaitForExit();
            //pabcnetcProcess.WaitForExit(5000);
            if (!pabcnetcProcess.HasExited) { // убить процесс если он работвет больше 5 секунд. Скорее всего он завис
                pabcnetcProcess.Kill();
                outputstring.AppendLine("Программа завершена. Она работала более 5 секунд и, вероятно, зависла");
            }
            return outputstring.ToString();
        }
        static void Main(string[] args)
        {
            //if (args.Length < 2)
            //{
            //    Console.WriteLine("No arguments!");
            //    Console.ReadKey();
            //    return;
            //}
            Console.WriteLine("Server start");
            var server = new ResponseSocket();
            var output = new PublisherSocket();
            //server.Bind("tcp://*:" + args[0]); // 5557
            //output.Bind("tcp://*:" + args[1]); // 5558
            server.Bind("tcp://*:5557");
            output.Bind("tcp://*:5558");

            StringResourcesLanguage.LoadDefaultConfig();
            var c = new Compiler();
            try
            {
                while (true) {
                    var code = server.ReceiveFrameString();

                    var myfilename = Helper.CreateTempPas(code);
                    var myexefilename = Compile(c, myfilename);

                    if (myexefilename == null) {
                        var msg = "";
                        if (c.ErrorsList.Count > 0) {
                            var err = c.ErrorsList[0];
                            msg = Helper.EnhanceErrorMsg(err) + '\n';
                        }
                        server.SendFrame(msg);
                        continue;
                    }

                    server.SendFrame("[OK]");
                    myexefilename = myexefilename.Replace(".pas", ".exe");
                    RunProcess(myexefilename, output);


                    output.SendFrame("[END]");

                    //server.SendFrame(output);
                }
            }
            catch (Exception e) {
                System.Console.WriteLine(e);
            }
            //readln;
            server.Dispose();
            }
    }
}
