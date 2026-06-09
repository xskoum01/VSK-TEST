using System;
using Microsoft.Xrm.Sdk;

namespace Test.PluginTest
{
    public class AccountWebsiteChangeMarker : IPlugin
    {
        private const string AccountEntityName = "account";
        private const string WebsiteUrlField = "websiteurl";
        private const string DescriptionField = "description";
        private const int PreOperationStage = 20;

        public void Execute(IServiceProvider serviceProvider)
        {
            ITracingService tracer = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            IPluginExecutionContext context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));

            bool isExpectedContext = context.Stage == PreOperationStage
                && context.PrimaryEntityName == AccountEntityName;

            if (isExpectedContext && context.InputParameters.Contains("Target") && context.InputParameters["Target"] is Entity)
            {
                Entity target = (Entity)context.InputParameters["Target"];

                if (target.Contains(WebsiteUrlField))
                {
                    string timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
                    target[DescriptionField] = $"Website changed by test plugin at {timestamp}";
                    tracer.Trace("Description set: {0}", target[DescriptionField]);
                }
                else
                {
                    tracer.Trace("Target does not contain websiteurl. No action taken.");
                }
            }
            else if (!isExpectedContext)
            {
                tracer.Trace("Unexpected plugin context. Entity: {0}, Stage: {1}. No action taken.",
                    context.PrimaryEntityName, context.Stage);
            }
            else
            {
                tracer.Trace("Target is not present or not of type Entity. No action taken.");
            }
        }
    }
}
