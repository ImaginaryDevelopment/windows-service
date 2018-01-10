using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using Topshelf;
using Topshelf.Builders;
namespace ConsoleApp2
{
    public class TownCrier
    {
        readonly Timer _timer;
        public static void LogAction(string fileName, string msg, bool append)
        {
            // in case building copies somewhere fun, hardcode output path to somewhere we know where to look, since we haven't hooked up any event logging yet
            var path = @"C:\Projects\ConsoleApp2\ConsoleApp2\bin\";
            Console.WriteLine($"{msg}?!??");
            var fullPath = System.IO.Path.Combine(path, fileName);
            if (!append)
                System.IO.File.WriteAllText(fullPath, msg);
            else
                System.IO.File.AppendAllText(fullPath, msg);
        }
        public TownCrier()
        {
            var recorded = false;
            _timer = new Timer(1000) { AutoReset = true };
            _timer.Elapsed += (sender, eventArgs) =>
            {
                if (!recorded)
                {
                    recorded = true;
                    LogAction("running.log", $"Engaged starting at {DateTime.Now}",true);
                }
                Console.WriteLine("It is {0} and all is well", DateTime.Now);
            };
        }
        public void Start() {
            _timer.Start();
        }
        public void Stop() { _timer.Stop(); }
    }
    class Program
    {

        public static void Main()
        {
            var rc = HostFactory.Run(x =>                                   //1
            {
                x.BeforeInstall(() => TownCrier.LogAction("beforeInstall.log",
                    $"Service about to install at {DateTime.Now}", true));
                x.AfterInstall(() => TownCrier.LogAction("afterInstall.log",
                        $"Service installed at {DateTime.Now}", true));
                x.AfterUninstall(() => TownCrier.LogAction("afterUninstall.log",
                    $"uninstalled at {DateTime.Now}", true));
                x.StartAutomatically();

                // thought this would make it install, it does not.
                // instead have to run `ConsoleApp2.exe install`
                x.EnableServiceRecovery(sr =>
                    sr.RestartService(1));
                x.Service<TownCrier>(s =>                                   //2
                {
                    s.ConstructUsing(name => new TownCrier());              //3
                    s.WhenStarted(tc => tc.Start());                        //4
                    s.WhenStopped(tc => tc.Stop());                         //5
                });
                x.RunAsLocalSystem();                                       //6
                // not sure if any of these have to match exe name, but maybe
                x.SetDescription("ConsoleApp2");                            //7
                x.SetDisplayName("ConsoleApp2");                            //8
                x.SetServiceName("ConsoleApp2");                            //9
            });                                                             //10

            var exitCode = (int)Convert.ChangeType(rc, rc.GetTypeCode());   //11
            Environment.ExitCode = exitCode;
        }

    }
}
