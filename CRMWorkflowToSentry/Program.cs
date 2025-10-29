using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sentry;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk;
using CommandLine;
using System.IO;

namespace CRMWorkflowToSentry
{
    class Program
    {
        static void Main(string[] args)
        {
            Parser.Default.ParseArguments<CommandLineArgs>(args).WithParsed((CommandLineArgs c) =>
            {
                Process(c);
                Console.WriteLine("Done");
            });
        }
        public static void Process(CommandLineArgs args)
        {
            DateTime startTime = DateTime.Now;
            DateTime filterStart = DateTime.MinValue;
            if (args.UseSmartFilter && File.Exists("timestamp.txt"))
            {
                DateTime.TryParse(File.ReadAllText("timestamp.txt"), out filterStart);
            }
            
            // Initialize Sentry
            using (SentrySdk.Init(options =>
            {
                options.Dsn = args.SentryDSN;
            }))
            {
                Console.WriteLine("Connecting to Dynamics CRM");
                ServiceClient service = new ServiceClient(args.CRMConnectionString);
                Console.WriteLine("Looking for failed sync workflows");
                ReportSyncWorkflowErrors(service, args.Days, filterStart);
                Console.WriteLine("Looking for failed async workflows");
                ReportAsyncWorkflowErrors(service, args.Days, filterStart);
                File.WriteAllText("timestamp.txt", startTime.ToString());
            }
        }

        private static void ReportSyncWorkflowErrors(ServiceClient service, int days, DateTime filterStart)
        {
            FetchExpression fetch = new FetchExpression($@"
                <fetch version=""1.0"" output-format=""xml-platform"" mapping=""logical"" distinct=""false"">
                  <entity name=""processsession"">
                    <attribute name=""processsessionid"" />
                    <attribute name=""startedon"" />
                    <attribute name=""regardingobjectid"" />
                    <attribute name=""comments"" />
                    <attribute name=""createdby"" />
                    <attribute name=""modifiedon"" />
                    <order attribute=""startedon"" descending=""true"" />
                    <filter type=""and"">
                      <condition attribute=""statecode"" operator=""eq"" value=""1"" />
                      <condition attribute=""statuscode"" operator=""eq"" value=""6"" />
                      <condition attribute=""modifiedon"" operator=""last-x-days"" value=""{days}""/>
                    </filter>
                    <link-entity name=""workflow"" from=""workflowid"" to=""processid"" visible=""false"" link-type=""outer"" alias=""link_workflow"">
                      <attribute name=""name"" />
                      <attribute name=""category"" />
                    </link-entity>
                    <link-entity name=""systemuser"" from=""systemuserid"" to=""executedby"" visible=""false"" link-type=""outer"" alias=""link_systemuser"">
                      <attribute name=""fullname"" />
                    </link-entity>
                  </entity>
                </fetch>
            ");
            var result = service.RetrieveMultiple(fetch);
            foreach (var item in result.Entities.Where(d => (DateTime)d["modifiedon"] >= filterStart))
            {
                string workflowName = "Unknown workflow";
                if (item.Contains("link_workflow.name"))
                {
                    workflowName = (string)((AliasedValue)item["link_workflow.name"]).Value;
                }
                var e = new Exception((string)item["comments"]);
                e.Source = workflowName;
                e.Data["workflow"] = workflowName;
                e.Data["target"] = item.Contains("regardingobjectid") ? (EntityReference)item["regardingobjectid"] : null;
                e.Data["timestamp"] = (DateTime)item["startedon"];
                e.Data["username"] = ((EntityReference)item["createdby"]).Name;
                e.Data["type"] = "sync";
                
                SentrySdk.CaptureException(e, scope =>
                {
                    scope.SetTag("username", ((EntityReference)item["createdby"]).Name);
                    scope.SetTag("workflow", workflowName);
                    scope.Contexts["Workflow"] = new
                    {
                        name = workflowName,
                        target = item.Contains("regardingobjectid") ? ((EntityReference)item["regardingobjectid"]).LogicalName : null,
                        targetId = item.Contains("regardingobjectid") ? (Guid?)((EntityReference)item["regardingobjectid"]).Id : null,
                        timestamp = (DateTime)item["startedon"],
                        type = "sync"
                    };
                });
            }
        }
        private static void ReportAsyncWorkflowErrors(IOrganizationService service, int days, DateTime filterStart)
        {
            FetchExpression fetch = new FetchExpression($@"
                <fetch version=""1.0"" output-format=""xml-platform"" mapping=""logical"" distinct=""false"">
                  <entity name=""asyncoperation"">
                    <attribute name=""name""/>
                    <attribute name=""regardingobjectid""/>
                    <attribute name=""message""/>
                    <attribute name=""data""/>
                    <attribute name=""errorcode""/>
                    <attribute name=""friendlymessage""/>
                    <attribute name=""startedon""/>
                    <attribute name=""createdby""/>
                    <attribute name=""modifiedon"" />
                    <filter type=""and"">
                      <condition attribute=""operationtype"" operator=""eq"" value=""10""/>
                      <condition attribute=""errorcode"" operator=""not-null""/>
                      <condition attribute=""modifiedon"" operator=""last-x-days"" value=""{days}""/>
                    </filter>
                  </entity>
                </fetch>
            ");
            var result = service.RetrieveMultiple(fetch);
            foreach (var item in result.Entities.Where(d => (DateTime)d["modifiedon"] >= filterStart))
            {
                var e = new Exception((string)item["message"]);
                e.Source = (string)item["name"];
                e.Data["workflow"] = item["name"];
                e.Data["target"] = (EntityReference)item["regardingobjectid"];
                e.Data["timestamp"] = (DateTime)item["startedon"];
                e.Data["username"] = ((EntityReference)item["createdby"]).Name;
                e.Data["errorcode"] = item["errorcode"];
                e.Data["type"] = "async";
                if (item.Contains("data"))
                {
                    e.Data["data"] = item["data"];
                }
                
                SentrySdk.CaptureException(e, scope =>
                {
                    scope.SetTag("username", ((EntityReference)item["createdby"]).Name);
                    scope.SetTag("workflow", (string)item["name"]);
                    scope.Contexts["Workflow"] = new
                    {
                        name = (string)item["name"],
                        target = ((EntityReference)item["regardingobjectid"]).LogicalName,
                        targetId = ((EntityReference)item["regardingobjectid"]).Id,
                        timestamp = (DateTime)item["startedon"],
                        errorcode = item["errorcode"],
                        type = "async",
                        data = item.Contains("data") ? item["data"] : null
                    };
                });
            }
        }
    }
}
