// Form script: opportunity
// Events: nvr_priceset OnChange, transactioncurrencyid OnChange, nvr_currrate OnChange
// Purpose: keep nvr_currrate in sync with the price list currency vs. the opportunity currency.

const CURRENCY_MISMATCH_NOTIFICATION_ID = "nvr_currency_mismatch";

const currencyMismatchText = {
    1033: "Enter the conversion rate for the opportunity currency against the price list currency.",
    1029: "Zadejte přepočtový kurz pro měnu oportunity vůči měně ceníku."
};

// Shared helper: reads priceset and currency, retrieves priceset currency via Web API,
// then sets nvr_currrate value, required level and notification accordingly.
var syncCurrencyRate = async function (formContext) {
    const nvr_priceset = formContext.getAttribute("nvr_priceset");
    const transactioncurrencyid = formContext.getAttribute("transactioncurrencyid");
    const nvr_currrate = formContext.getAttribute("nvr_currrate");
    const nvr_currrateControl = formContext.getControl("nvr_currrate");

    const priceSetValue = nvr_priceset ? nvr_priceset.getValue() : null;
    const currencyValue = transactioncurrencyid ? transactioncurrencyid.getValue() : null;

    if (!priceSetValue || priceSetValue.length === 0 || !currencyValue || currencyValue.length === 0) {
        // Required inputs missing: reset state, no notification needed
        if (nvr_currrate) {
            nvr_currrate.setRequiredLevel("none");
        }
        if (nvr_currrateControl) {
            nvr_currrateControl.clearNotification(CURRENCY_MISMATCH_NOTIFICATION_ID);
        }
    } else {
        const priceSetId = priceSetValue[0].id.replace(/[{}]/g, "").toLowerCase();
        const opportunityCurrencyId = currencyValue[0].id.replace(/[{}]/g, "").toLowerCase();

        try {
            const langId = Xrm.Utility.getGlobalContext().userSettings.languageId;
            const result = await Xrm.WebApi.retrieveRecord("nvr_priceset", priceSetId, "?$select=_nvr_currency_value");
            const priceSetCurrencyId = result ? String(result._nvr_currency_value || "").replace(/[{}]/g, "").toLowerCase() : "";

            const currenciesMatch = priceSetCurrencyId !== "" && priceSetCurrencyId === opportunityCurrencyId;

            if (currenciesMatch) {
                if (nvr_currrate) {
                    nvr_currrate.setValue(1);
                    nvr_currrate.setRequiredLevel("none");
                }
                if (nvr_currrateControl) {
                    nvr_currrateControl.clearNotification(CURRENCY_MISMATCH_NOTIFICATION_ID);
                }
            } else {
                if (nvr_currrate) {
                    nvr_currrate.setValue(null);
                    nvr_currrate.setRequiredLevel("required");
                }
                if (nvr_currrateControl) {
                    const notifText = currencyMismatchText[langId] || currencyMismatchText[1033];
                    nvr_currrateControl.setNotification(notifText, CURRENCY_MISMATCH_NOTIFICATION_ID);
                }
            }
        } catch (error) {
            console.error("[NVR] syncCurrencyRate: failed to retrieve nvr_priceset.", error);
        }
    }
};

// Triggered when nvr_priceset changes.
var nvr_priceset_OnChange = async function (executionContext) {
    const formContext = executionContext.getFormContext();
    await syncCurrencyRate(formContext);
};

// Triggered when transactioncurrencyid changes.
var nvr_transactioncurrencyid_OnChange = async function (executionContext) {
    const formContext = executionContext.getFormContext();
    await syncCurrencyRate(formContext);
};

// Triggered when nvr_currrate changes.
// If the user enters a positive rate, the mismatch notification is no longer relevant.
var nvr_currrate_OnChange = function (executionContext) {
    const formContext = executionContext.getFormContext();
    const nvr_currrate = formContext.getAttribute("nvr_currrate");
    const nvr_currrateControl = formContext.getControl("nvr_currrate");

    const currrateValue = nvr_currrate ? nvr_currrate.getValue() : null;

    if (currrateValue !== null && currrateValue > 0) {
        if (nvr_currrateControl) {
            nvr_currrateControl.clearNotification(CURRENCY_MISMATCH_NOTIFICATION_ID);
        }
    }
};
