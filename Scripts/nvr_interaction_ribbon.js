// Ribbon script: nvr_interaction
// Button: run report for selected interactions
// Action:  runReportForSelectedInteractions_Action
// Enable:  runReportForSelectedInteractions_EnableRule
//
// Required Ribbon parameters:
//   Action:      SelectedControlSelectedItemIds (CSV string of selected record GUIDs)
//   EnableRule:  SelectedControlSelectedItemReferences (array of selected item references)
//
// Creates an nvr_powerautomateintegrationlog record carrying a FetchXML
// built from the selected nvr_interaction IDs. No Logic App URL, no fetch().

const INTEGRATION_LOG_ENTITY = "nvr_powerautomateintegrationlog";
const INTEGRATION_TYPE_REPORT = 917680010;

const interactionReportText = {
    1033: {
        noSelection: "Select at least one interaction record.",
        confirmTitle: "Generate Report",
        confirmText: "Generate a report for the selected interactions?",
        confirmYes: "Yes",
        confirmNo: "No",
        submitted: "Report request submitted successfully.",
        error: "An error occurred while submitting the report request."
    },
    1029: {
        noSelection: "Vyberte alespoň jeden záznam interakce.",
        confirmTitle: "Generovat report",
        confirmText: "Vygenerovat report pro vybrané interakce?",
        confirmYes: "Ano",
        confirmNo: "Ne",
        submitted: "Požadavek na report byl úspěšně odeslán.",
        error: "Při odesílání požadavku na report došlo k chybě."
    }
};

// Build FetchXML selecting nvr_interaction records by activityid IN list.
var buildInteractionFetchXml = function (ids) {
    let valueNodes = "";
    for (let i = 0; i < ids.length; i++) {
        valueNodes += "<value>" + ids[i] + "</value>";
    }
    return "<fetch><entity name=\"nvr_interaction\"><attribute name=\"activityid\"/>"
        + "<filter type=\"and\"><condition attribute=\"activityid\" operator=\"in\">"
        + valueNodes
        + "</condition></filter></entity></fetch>";
};

// Ribbon action: triggered when the button is clicked from the subgrid/view.
// selectedControlSelectedItemIds — CSV of selected record GUIDs from Ribbon Workbench.
var runReportForSelectedInteractions_Action = async function (selectedControlSelectedItemIds) {
    const langId = Xrm.Utility.getGlobalContext().userSettings.languageId;
    const message = interactionReportText[langId] || interactionReportText[1033];

    // Normalize IDs from CSV string
    const rawParts = String(selectedControlSelectedItemIds || "").split(",");
    const ids = [];
    for (let i = 0; i < rawParts.length; i++) {
        const cleanId = rawParts[i].replace(/[{}]/g, "").trim().toLowerCase();
        if (cleanId) {
            ids.push(cleanId);
        }
    }

    if (ids.length === 0) {
        await Xrm.Navigation.openAlertDialog({ text: message.noSelection });
    } else {
        const confirmResult = await Xrm.Navigation.openConfirmDialog(
            {
                title: message.confirmTitle,
                text: message.confirmText,
                confirmButtonLabel: message.confirmYes,
                cancelButtonLabel: message.confirmNo
            }
        );

        if (confirmResult && confirmResult.confirmed) {
            try {
                const fetchXml = buildInteractionFetchXml(ids);
                await Xrm.WebApi.createRecord(INTEGRATION_LOG_ENTITY, {
                    nvr_fetchxml: fetchXml,
                    nvr_integrationtype: INTEGRATION_TYPE_REPORT
                });
                await Xrm.Navigation.openAlertDialog({ text: message.submitted });
            } catch (error) {
                console.error("[NVR] runReportForSelectedInteractions_Action: createRecord failed.", error);
                await Xrm.Navigation.openAlertDialog({ text: message.error });
            }
        }
    }
};

// Ribbon enable rule: button is active only when at least one row is selected
// and every selected item belongs to nvr_interaction.
// selectedItems — SelectedControlSelectedItemReferences from Ribbon Workbench.
var runReportForSelectedInteractions_EnableRule = function (selectedItems) {
    let isEnabled = false;

    if (selectedItems && selectedItems.length > 0) {
        let allAreInteractions = true;

        for (let i = 0; i < selectedItems.length; i++) {
            if (!selectedItems[i] || selectedItems[i].TypeName !== "nvr_interaction") {
                allAreInteractions = false;
                break;
            }
        }

        isEnabled = allAreInteractions;
    }

    return isEnabled;
};
