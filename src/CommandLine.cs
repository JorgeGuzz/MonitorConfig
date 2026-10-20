using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace MonitorConfig
{
    // "MonitorConfig.exe --brightness 40" and friends (CommandRunner): handed to the copy already running, or done here
    // with the same services, a synchronous DDC queue, and the monitor told to store the change before exiting.
    static class CommandLine
    {
        public static int Run(string[] args, string mutexName)
        {
            Native.AttachConsole(Native.ATTACH_PARENT_PROCESS);   // a windowed exe prints to the console it was started from
            CommandRunner.Result result = null;
            Mutex running;
            if (Mutex.TryOpenExisting(mutexName, out running))
            {
                running.Dispose();
                result = CommandServer.Send(CommandServer.DefaultPipe, args, 3000);
            }
            if (result == null) result = RunHere(args);
            if (result.Text.Length > 0) (result.Code == 0 ? Console.Out : Console.Error).WriteLine(result.Text);
            return result.Code;
        }

        static CommandRunner.Result RunHere(string[] args)
        {
            var settings = Settings.Load();
            Crash.Folder = Path.GetDirectoryName(settings.Path);
            Lang.Load(settings.Language);
            Func<DateTime> clock = () => DateTime.Now;
            var queue = DdcQueue.Manual();
            var service = new MonitorService(settings, new Dxva2Bus(), queue, a => a(), clock);
            var saver = new SaveScheduler(service, clock);
            var profiles = new ProfileService(service);
            var labels = new InputLabels(settings);
            string failure = null;
            service.StatusChanged += (text, error) => { if (error) failure = text; };
            try
            {
                service.Detect(false);
                queue.RunPending();
                failure = null;   // detection notes ("no DDC/CI") are part of the command's own answer
                var needed = CommandRunner.CodesNeeded(args);
                foreach (var m in service.All.Where(x => x.Responsive)) service.Read(m, needed, null);
                queue.RunPending();
                var result = CommandRunner.Run(args, service, profiles, labels, settings);
                queue.RunPending();
                if (failure != null && result.Code == 0) result = new CommandRunner.Result { Code = 1, Text = result.Text + "\n" + failure };
                return result;
            }
            finally
            {
                saver.Dispose();     // stores the changes in the monitors' memory
                service.Dispose();   // runs what is left and closes the monitors
            }
        }
    }
}
