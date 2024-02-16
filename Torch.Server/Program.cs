using System;
using System.IO;
#if NETFRAMEWORK
using System.Runtime.InteropServices;
using System.ServiceProcess;
#endif
using NLog;
using NLog.Targets;
using Torch.Utils;

namespace Torch.Server;

internal static class Program
{
    /// <remarks>
    /// This method must *NOT* load any types/assemblies from the vanilla game, otherwise automatic updates will fail.
    /// </remarks>
    [STAThread]
    public static void Main(string[] args)
    {
        Target.Register<FlowDocumentTarget>("FlowDocument");

        var workingDir = new FileInfo(typeof(Program).Assembly.Location).Directory.ToString();
        var binDir = Path.Combine(workingDir, "DedicatedServer64");
        var assemblyResolver = new TorchAssemblyResolver(binDir);

        try
        {
#if NETFRAMEWORK
            // Breaks on Windows Server 2019
            if (!RuntimeInformation.OSDescription.Contains("Server 2019")
                && !RuntimeInformation.OSDescription.Contains("Server 2022")
                && !Environment.UserInteractive)
            {
                using (var service = new TorchService(args))
                    ServiceBase.Run(service);

                return;
            }
#endif

            var initializer = new Initializer(workingDir);

            if (!initializer.Initialize(args))
                return;

            initializer.Run();
        }
        catch (Exception runException)
        {
            var log = LogManager.GetCurrentClassLogger();
            log.Fatal(runException.ToString());
        }
    }
}
