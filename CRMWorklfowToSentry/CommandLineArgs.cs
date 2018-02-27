using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine.Text;
using CommandLine;

namespace CRMWorklfowToSentry
{
    class CommandLineArgs 
    {
        [Option('c',"crm", Required = true, HelpText = "CRM connection string")]
        public string CRMConnectionString { get; set; }

        [Option('s',"sentry", Required = true, HelpText = "Sentry DSN")]
        public string SentryDSN { get; set; }

        [Option("days", Required = true, HelpText = "How many days to grab data for")]
        public int Days { get; set; }

        [Option("solution", HelpText = "Solution Name for version (not currently implemented)")]
        public string SolutionName { get; set; }

    }
}
