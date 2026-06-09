using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Navertica.ZlrOpportunity
{
    /// <summary>
    /// Plugin: TransferOpportunityPricesToFinalProductsOnDelivered
    /// Purpose:
    ///   When an Opportunity transitions to Delivered (statecode = 1), transfers selected financial
    ///   fields from the Opportunity to all related nvr_finalproduct records and records the transfer date.
    ///   The nvr_containerid product name (body BC No) is transferred only when nvr_bodybrand is "Zoeller".
    /// Execution:
    ///   - Entity: opportunity
    ///   - Trigger: Update
    ///   - Pipeline: PostOperation
    ///   - Sync/Async: Asynchronous
    /// Registration:
    ///   - Filtering attributes: statecode
    ///   - Required PreImage: PreImage(statecode)
    ///   - Required PostImage: PostImage(nvr_bodymargin, nvr_bodyprice, nvr_calcbodycost,
    ///       nvr_calclifterunitcost, nvr_calcvehiclecost, nvr_containerid, nvr_eurratecalc,
    ///       nvr_liftermargin, nvr_lifterunitprice, nvr_loaderid, nvr_vehicleid,
    ///       nvr_vehiclemargin, nvr_vehicleprice, nvr_bodybrand)
    /// Notes:
    ///   PostImage provides all source field values; no extra Retrieve on the Opportunity.
    ///   Lookup names are resolved before the nvr_finalproduct loop to minimise DB calls.
    ///   All target fields are left unchanged when the source value is null (no field clearing).
    /// </summary>
    public class TransferOpportunityPricesToFinalProductsOnDelivered : IPlugin
    {
        private const string OpportunityEntityName = "opportunity";
        private const string FinalProductEntityName = "nvr_finalproduct";
        private const string ProductEntityName = "product";

        private const string PreImageName = "PreImage";
        private const string PostImageName = "PostImage";

        private const string StateCodeField = "statecode";
        private const int StateCodeDelivered = 1;

        // Opportunity source fields
        private const string BodyMarginField = "nvr_bodymargin";
        private const string BodyPriceField = "nvr_bodyprice";
        private const string CalcBodyCostField = "nvr_calcbodycost";
        private const string CalcLifterUnitCostField = "nvr_calclifterunitcost";
        private const string CalcVehicleCostField = "nvr_calcvehiclecost";
        private const string ContainerIdField = "nvr_containerid";
        private const string EurRateCalcField = "nvr_eurratecalc";
        private const string LifterMarginField = "nvr_liftermargin";
        private const string LifterUnitPriceField = "nvr_lifterunitprice";
        private const string LoaderIdField = "nvr_loaderid";
        private const string VehicleIdField = "nvr_vehicleid";
        private const string VehicleMarginField = "nvr_vehiclemargin";
        private const string VehiclePriceField = "nvr_vehicleprice";
        private const string BodyBrandField = "nvr_bodybrand";

        // nvr_finalproduct relationship field
        private const string QuoteField = "nvr_quote";

        // nvr_finalproduct target fields
        // nvr_bodyprice and nvr_vehicleprice share the same logical name on both entities
        private const string BodyProfField = "nvr_bodyprof";
        private const string BodyCostField = "nvr_bodycost";
        private const string LifterCostField = "nvr_liftercost";
        private const string VehicleCostField = "nvr_vehiclecost";
        private const string BodyBcNoField = "nvr_bodybcno";
        private const string EurRateCField = "nvr_eurratec";
        private const string LifterProfField = "nvr_lifterprof";
        private const string LifterPriceField = "nvr_lifterprice";
        private const string LifterBcNoField = "nvr_lifterbcno";
        private const string VehicleBcNoField = "nvr_vehiclebcno";
        private const string VehicleProfField = "nvr_vehicleprof";
        private const string PriceTransfDateField = "nvr_pricetransfdate";

        private const string ZoellerBrandValue = "Zoeller";
        private const string ProductNameField = "name";

        public void Execute(IServiceProvider serviceProvider)
        {
            ITracingService tracer = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            IPluginExecutionContext context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            IOrganizationServiceFactory serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            IOrganizationService service = serviceFactory.CreateOrganizationService(context.UserId);

            tracer.Trace("TransferOpportunityPricesToFinalProductsOnDelivered: started.");

            if (context.PrimaryEntityName != OpportunityEntityName)
            {
                throw new InvalidPluginExecutionException(
                    $"Invalid plugin registration. Expected entity '{OpportunityEntityName}', got '{context.PrimaryEntityName}'.");
            }

            if (context.InputParameters.Contains("Target") && context.InputParameters["Target"] is Entity)
            {
                Entity target = (Entity)context.InputParameters["Target"];

                Entity preImage = context.PreEntityImages.Contains(PreImageName)
                    ? context.PreEntityImages[PreImageName]
                    : null;

                if (preImage == null)
                {
                    tracer.Trace("PreImage '{0}' not found. Cannot evaluate statecode transition. No action taken.", PreImageName);
                }
                else
                {
                    Entity postImage = context.PostEntityImages.Contains(PostImageName)
                        ? context.PostEntityImages[PostImageName]
                        : null;

                    if (postImage == null)
                    {
                        tracer.Trace("PostImage '{0}' not found. Cannot read source fields. No action taken.", PostImageName);
                    }
                    else if (IsDeliveredTransition(preImage, target))
                    {
                        tracer.Trace("Delivered transition detected. Resolving lookup names.");

                        string containerName = ResolveLookupName(service, postImage.GetAttributeValue<EntityReference>(ContainerIdField));
                        string loaderName = ResolveLookupName(service, postImage.GetAttributeValue<EntityReference>(LoaderIdField));
                        string vehicleName = ResolveLookupName(service, postImage.GetAttributeValue<EntityReference>(VehicleIdField));

                        string bodyBrand = (postImage.GetAttributeValue<string>(BodyBrandField) ?? string.Empty).Trim();
                        bool isZoellerBrand = string.Equals(bodyBrand, ZoellerBrandValue, StringComparison.OrdinalIgnoreCase);

                        tracer.Trace("nvr_bodybrand='{0}', isZoellerBrand={1}.", bodyBrand, isZoellerBrand);

                        EntityCollection finalProducts = QueryRelatedFinalProducts(service, target.Id);

                        tracer.Trace("Found {0} related nvr_finalproduct record(s).", finalProducts.Entities.Count);

                        foreach (Entity finalProduct in finalProducts.Entities)
                        {
                            Entity update = BuildFinalProductUpdate(finalProduct.Id, postImage, containerName, loaderName, vehicleName, isZoellerBrand);
                            service.Update(update);
                        }

                        tracer.Trace("Transfer complete. Updated {0} record(s).", finalProducts.Entities.Count);
                    }
                    else
                    {
                        tracer.Trace("No Delivered transition detected. Exiting.");
                    }
                }
            }
            else
            {
                tracer.Trace("Target is missing or not an Entity. Exiting.");
            }
        }

        private bool IsDeliveredTransition(Entity preImage, Entity target)
        {
            OptionSetValue previousState = preImage.GetAttributeValue<OptionSetValue>(StateCodeField);
            OptionSetValue newState = target.GetAttributeValue<OptionSetValue>(StateCodeField);

            bool wasNotDelivered = previousState == null || previousState.Value != StateCodeDelivered;
            bool isNowDelivered = newState != null && newState.Value == StateCodeDelivered;

            return wasNotDelivered && isNowDelivered;
        }

        private string ResolveLookupName(IOrganizationService service, EntityReference reference)
        {
            string name = null;

            if (reference != null)
            {
                if (reference.Name != null)
                {
                    name = reference.Name;
                }
                else
                {
                    Entity product = service.Retrieve(ProductEntityName, reference.Id, new ColumnSet(ProductNameField));
                    name = product?.GetAttributeValue<string>(ProductNameField);
                }
            }

            return name;
        }

        private EntityCollection QueryRelatedFinalProducts(IOrganizationService service, Guid opportunityId)
        {
            var query = new QueryExpression(FinalProductEntityName)
            {
                ColumnSet = new ColumnSet(false),
                Criteria =
                {
                    Conditions =
                    {
                        new ConditionExpression(QuoteField, ConditionOperator.Equal, opportunityId)
                    }
                }
            };

            return service.RetrieveMultiple(query);
        }

        private Entity BuildFinalProductUpdate(
            Guid finalProductId,
            Entity source,
            string containerName,
            string loaderName,
            string vehicleName,
            bool isZoellerBrand)
        {
            Entity update = new Entity(FinalProductEntityName, finalProductId);

            // Decimal fields: leave unchanged when source value is null
            if (source.Contains(BodyMarginField) && source[BodyMarginField] != null)
            {
                update[BodyProfField] = source.GetAttributeValue<decimal>(BodyMarginField);
            }

            if (source.Contains(CalcBodyCostField) && source[CalcBodyCostField] != null)
            {
                update[BodyCostField] = source.GetAttributeValue<decimal>(CalcBodyCostField);
            }

            if (source.Contains(CalcVehicleCostField) && source[CalcVehicleCostField] != null)
            {
                update[VehicleCostField] = source.GetAttributeValue<decimal>(CalcVehicleCostField);
            }

            if (source.Contains(EurRateCalcField) && source[EurRateCalcField] != null)
            {
                update[EurRateCField] = source.GetAttributeValue<decimal>(EurRateCalcField);
            }

            if (source.Contains(LifterMarginField) && source[LifterMarginField] != null)
            {
                update[LifterProfField] = source.GetAttributeValue<decimal>(LifterMarginField);
            }

            if (source.Contains(VehicleMarginField) && source[VehicleMarginField] != null)
            {
                update[VehicleProfField] = source.GetAttributeValue<decimal>(VehicleMarginField);
            }

            // Money -> Decimal fields: leave unchanged when source value is null
            Money bodyPrice = source.GetAttributeValue<Money>(BodyPriceField);
            if (bodyPrice != null)
            {
                update[BodyPriceField] = bodyPrice.Value;
            }

            Money calcLifterUnitCost = source.GetAttributeValue<Money>(CalcLifterUnitCostField);
            if (calcLifterUnitCost != null)
            {
                update[LifterCostField] = calcLifterUnitCost.Value;
            }

            Money lifterUnitPrice = source.GetAttributeValue<Money>(LifterUnitPriceField);
            if (lifterUnitPrice != null)
            {
                update[LifterPriceField] = lifterUnitPrice.Value;
            }

            Money vehiclePrice = source.GetAttributeValue<Money>(VehiclePriceField);
            if (vehiclePrice != null)
            {
                update[VehiclePriceField] = vehiclePrice.Value;
            }

            // Lookup -> Text fields: leave unchanged when name is null
            if (loaderName != null)
            {
                update[LifterBcNoField] = loaderName;
            }

            if (vehicleName != null)
            {
                update[VehicleBcNoField] = vehicleName;
            }

            // Conditional: body BC No transferred only when brand is Zoeller
            if (isZoellerBrand && containerName != null)
            {
                update[BodyBcNoField] = containerName;
            }

            // Always write transfer timestamp
            update[PriceTransfDateField] = DateTime.UtcNow;

            return update;
        }
    }
}
