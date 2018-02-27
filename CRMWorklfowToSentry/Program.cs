using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SharpRaven;
using SharpRaven.Data;
using Microsoft.Xrm.Tooling.Connector;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk;
using CommandLine;

namespace CRMWorklfowToSentry
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
            var ravenClient = new RavenClient(args.SentryDSN);
            Console.WriteLine("Connecting to Dynamics CRM");
            CrmServiceClient service = new CrmServiceClient(args.CRMConnectionString);
            Console.WriteLine("Looking for failed sync workflows");
            ReportSyncWorkflowErrors(ravenClient, service, args.Days);
            Console.WriteLine("Looking for failed async workflows");
            ReportAsyncWorkflowErrors(ravenClient, service, args.Days);
        }

        private static void ReportSyncWorkflowErrors(RavenClient ravenClient, CrmServiceClient service, int days)
        {
            FetchExpression fetch = new FetchExpression($@"
                <fetch version=""1.0"" output-format=""xml-platform"" mapping=""logical"" distinct=""false"">
                  <entity name=""processsession"">
                    <attribute name=""processsessionid"" />
                    <attribute name=""startedon"" />
                    <attribute name=""regardingobjectid"" />
                    <attribute name=""comments"" />
                    <attribute name=""createdby"" />
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
            foreach (var item in result.Entities)
            {
                string workflowName = "Unknown workflow";
                if (item.Contains("link_workflow.name"))
                {
                    workflowName = (string)((AliasedValue)item["link_workflow.name"]).Value;
                }
                var e = new Exception((string)item["comments"]);
                e.Source = workflowName;
                e.Data["workflow"] = workflowName;
                e.Data["target"] = (EntityReference)item["regardingobjectid"];
                e.Data["timestamp"] = (DateTime)item["startedon"];
                e.Data["username"] = ((EntityReference)item["createdby"]).Name;
                e.Data["type"] = "sync";
                SentryEvent sentryEvent = new SentryEvent(e);
                sentryEvent.Tags["username"] = ((EntityReference)item["createdby"]).Name;
                sentryEvent.Tags["workflow"] = workflowName;
                ravenClient.BeforeSend = requester =>
                {
                    requester.Packet.TimeStamp = (DateTime)item["startedon"];
                    return requester;
                };
                ravenClient.Capture(sentryEvent);
            }
        }
        private static void ReportAsyncWorkflowErrors( RavenClient ravenClient, IOrganizationService service, int days)
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
                    <filter type=""and"">
                      <condition attribute=""operationtype"" operator=""eq"" value=""10""/>
                      <condition attribute=""errorcode"" operator=""not-null""/>
                      <condition attribute=""modifiedon"" operator=""last-x-days"" value=""{days}""/>
                    </filter>
                  </entity>
                </fetch>
            ");
            var result = service.RetrieveMultiple(fetch);
            foreach (var item in result.Entities)
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
                SentryEvent sentryEvent = new SentryEvent(e);
                sentryEvent.Tags["username"] = ((EntityReference)item["createdby"]).Name;
                sentryEvent.Tags["workflow"] = (string)item["name"];
                ravenClient.BeforeSend = requester =>
                {
                    requester.Packet.TimeStamp = (DateTime)item["startedon"];
                    return requester;
                };
                ravenClient.Capture(sentryEvent);
            }
        }
    }
}
