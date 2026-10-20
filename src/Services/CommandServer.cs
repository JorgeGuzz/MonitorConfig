using System;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;

namespace MonitorConfig
{
    // Lets "MonitorConfig.exe --brightness 40" reach the copy already running, so every DDC/CI transaction goes through
    // its one queue and its windows stay in sync. A named pipe that only the same Windows user can open; one request
    // (the arguments) and one answer (exit code + text) per connection.
    sealed class CommandServer : IDisposable
    {
        const char Separator = '\u001F';
        readonly string pipeName;
        readonly Action<Action> post;
        readonly Func<string[], CommandRunner.Result> run;   // runs on the UI thread
        volatile bool stopping;

        public static string DefaultPipe { get { return "MonitorConfig." + WindowsIdentity.GetCurrent().User.Value; } }

        public CommandServer(string pipeName, Action<Action> post, Func<string[], CommandRunner.Result> run)
        {
            this.pipeName = pipeName;
            this.post = post;
            this.run = run;
            new Thread(Loop) { IsBackground = true, Name = "Commands" }.Start();
        }

        void Loop()
        {
            while (!stopping)
            {
                try
                {
                    using (var pipe = Create(pipeName))
                    {
                        pipe.WaitForConnection();
                        string request = ReadLine(pipe);
                        if (stopping || request == null) continue;
                        var answer = Execute(request.Split(Separator));
                        var bytes = new UTF8Encoding(false).GetBytes(answer.Code + "\n" + answer.Text);
                        pipe.Write(bytes, 0, bytes.Length);
                        pipe.Flush();
                        pipe.WaitForPipeDrain();
                    }
                }
                catch (Exception ex)
                {
                    if (stopping) return;
                    Crash.Log(ex);
                    Thread.Sleep(1000);
                }
            }
        }

        CommandRunner.Result Execute(string[] args)
        {
            CommandRunner.Result result = null;
            using (var done = new ManualResetEvent(false))
            {
                post(delegate
                {
                    try { result = run(args); }
                    catch (Exception ex) { Crash.Log(ex); result = new CommandRunner.Result { Code = 1, Text = ex.Message }; }
                    finally { done.Set(); }
                });
                if (!done.WaitOne(10000)) return new CommandRunner.Result { Code = 1, Text = "MonitorConfig did not answer." };
            }
            return result;
        }

        static NamedPipeServerStream Create(string name)
        {
            var security = new PipeSecurity();
            security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User, PipeAccessRights.FullControl, AccessControlType.Allow));
            return new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.None, 4096, 4096, security);
        }

        // Up to the first '\n' (the client sends one line).
        static string ReadLine(Stream s)
        {
            var bytes = new MemoryStream();
            int b;
            while ((b = s.ReadByte()) >= 0 && b != '\n') bytes.WriteByte((byte)b);
            return b < 0 && bytes.Length == 0 ? null : Encoding.UTF8.GetString(bytes.ToArray());
        }

        // The client side: null if no copy is listening.
        public static CommandRunner.Result Send(string pipeName, string[] args, int timeoutMs)
        {
            try
            {
                using (var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut))
                {
                    pipe.Connect(timeoutMs);
                    var request = new UTF8Encoding(false).GetBytes(string.Join(Separator.ToString(), args) + "\n");
                    pipe.Write(request, 0, request.Length);
                    pipe.Flush();
                    string answer;
                    using (var reader = new StreamReader(pipe, Encoding.UTF8)) answer = reader.ReadToEnd();
                    int nl = answer.IndexOf('\n');
                    int code;
                    if (nl < 0 || !int.TryParse(answer.Substring(0, nl), out code)) return null;
                    return new CommandRunner.Result { Code = code, Text = answer.Substring(nl + 1) };
                }
            }
            catch (TimeoutException) { return null; }
            catch (IOException) { return null; }
        }

        public void Dispose()
        {
            stopping = true;
            try   // wakes the thread waiting for a connection
            {
                using (var wake = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut)) wake.Connect(200);
            }
            catch (Exception) { }
        }
    }
}
