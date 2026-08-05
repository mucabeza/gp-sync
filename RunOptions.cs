using System;
using System.Collections.Generic;

namespace SalesforceDynamicsGPIntegration
{
    /// <summary>
    /// Command line switches. The scheduled task runs with --scheduled (fires several times a night
    /// and only does what is still pending); running the executable with no arguments is a manual run
    /// and always synchronizes.
    /// </summary>
    public class RunOptions
    {
        public bool Seal { get; private set; }
        public bool Scheduled { get; private set; }
        public bool Force { get; private set; }
        public bool Help { get; private set; }
        public List<string> UnknownArguments { get; } = new List<string>();

        /// <summary>Skip sync data settings that already synchronized successfully today.</summary>
        public bool SkipAlreadySucceeded
        {
            get { return Scheduled && !Force; }
        }

        public static RunOptions Parse(string[] args)
        {
            var options = new RunOptions();
            foreach (var arg in args ?? new string[0])
            {
                if (string.IsNullOrWhiteSpace(arg))
                {
                    continue;
                }

                switch (arg.Trim().ToLowerInvariant())
                {
                    case "--seal":
                        options.Seal = true;
                        break;
                    case "--scheduled":
                        options.Scheduled = true;
                        break;
                    case "--force":
                        options.Force = true;
                        break;
                    case "--help":
                    case "-h":
                    case "/?":
                        options.Help = true;
                        break;
                    default:
                        options.UnknownArguments.Add(arg);
                        break;
                }
            }
            return options;
        }

        public static void PrintUsage()
        {
            Console.WriteLine("SalesforceDynamicsGpIntegration - Dynamics GP to Salesforce sync");
            Console.WriteLine();
            Console.WriteLine("Usage: SalesforceDynamicsGpIntegration.exe [options]");
            Console.WriteLine();
            Console.WriteLine("  (no options)   Manual run: always synchronizes every active sync data setting.");
            Console.WriteLine("  --scheduled    Scheduled run: skips sync data settings that already");
            Console.WriteLine("                 synchronized successfully today. Used by the MaxSfGpSync task.");
            Console.WriteLine("  --force        Synchronize everything, ignoring the saved state.");
            Console.WriteLine("  --seal         Encode the secrets in appsettings.json and exit.");
            Console.WriteLine("  --help         Show this message.");
            Console.WriteLine();
            Console.WriteLine("Exit codes: 0 = completed (or nothing pending), 1 = at least one failure.");
        }
    }

    /// <summary>Result of a synchronization run, per sync data setting.</summary>
    public class SyncRunOutcome
    {
        public int Succeeded { get; set; }
        public int Skipped { get; set; }
        public int Failed { get; set; }

        /// <summary>Set when the run could not even get to the sync data settings.</summary>
        public bool Aborted { get; set; }

        public int ExitCode
        {
            get { return (Aborted || Failed > 0) ? 1 : 0; }
        }

        public string Summary
        {
            get { return $"{Succeeded} succeeded, {Skipped} skipped, {Failed} failed"; }
        }
    }
}
