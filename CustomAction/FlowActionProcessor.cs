using Newtonsoft.Json.Linq;
using Microsoft.Xrm.Sdk;
using System;
using System.Collections.Generic;
using Microsoft.Crm.Sdk.Messages;
using System.Linq;
namespace PowerAutomate.Actions
{
    public class FlowActionProcessor : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            IPluginExecutionContext context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            ITracingService tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));

            string flowsJson = context.InputParameters["FlowsInformation"]?.ToString();
            string operationId = context.InputParameters["OperationId"]?.ToString();
            string properties = context.InputParameters["Properties"]?.ToString(); // New dynamic input
            string[] listOfProperties = properties.Split(',');
            string filtersJson = context.InputParameters["Filters"]?.ToString();
            bool isTrigger = Convert.ToBoolean(context.InputParameters["IsTrigger"]);
            JObject flowDetails = JObject.Parse(flowsJson);
            JObject filters = !string.IsNullOrEmpty(filtersJson) ? JObject.Parse(filtersJson) : new JObject();

            var definition = flowDetails["properties"]["definition"];
            var actions = isTrigger ? definition["triggers"].Children() : definition["actions"].Children();
            bool flowHasAction = false;
            foreach (var action in actions)
            {
                var actionContent = action.First;
                if (FindMatchingAction(actionContent, operationId, filters, listOfProperties,tracingService))
                {
                    tracingService.Trace("Matching action found, stopping search.");
                    flowHasAction = true;
                    break;
                }
            }
            context.OutputParameters["FlowHasAction"] = flowHasAction;
        }

        private static bool FindMatchingAction(JToken actionContent, string operationId, JObject filters,string[] listOfProperties, ITracingService tracingService)
        {
            var inputs = actionContent["inputs"];
            if (inputs != null && inputs.Type == JTokenType.Object && inputs["host"] != null)
            {
                string flowOperationId = inputs["host"]["operationId"]?.ToString();
                if (operationId == flowOperationId)
                {
                    bool allConditionsMet = listOfProperties.All(prop => inputs["parameters"]?[prop] != null);
                    if(allConditionsMet)
                    {
                        foreach (var filter in filters)
                        {
                            JToken expectedValue = filter.Value;
                            JToken actualValue = inputs["parameters"]?[filter.Key];

                            if (!JToken.DeepEquals(expectedValue, actualValue))
                            {
                                allConditionsMet = false;
                                break; // Stop checking further if a mismatch is found
                            }
                        }
                    }
                    if (allConditionsMet)
                    {
                        tracingService.Trace($"Matching action found for operationId: {operationId}");
                        return true; // Stop recursion once we find a match
                    }
                }
            }

            // Check for nested actions inside Apply_to_each, etc.
            var nestedActions = actionContent["actions"]?.Children();
            if (nestedActions != null)
            {
                foreach (var nestedAction in nestedActions)
                {
                    if (FindMatchingAction(nestedAction.First, operationId, filters, listOfProperties, tracingService))
                    {
                        return true; // Stop searching further if a match is found
                    }
                }
            }
            return false;
        }
    }
}

