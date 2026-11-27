using System;
using Microsoft.Extensions.Localization;
using Resgrid.Model;
using Resgrid.Model.Certifications;
using Resgrid.Model.Checklists;
using Resgrid.Model.IncidentLogistics;
using Resgrid.Model.Inventories;
using Resgrid.Model.Invoicing;
using Resgrid.Model.WorkOrders;
using Strings = Resgrid.Localization.Areas.User;

namespace Resgrid.Web.Helpers
{
	/// <summary>
	/// The plain-language name of a workflow trigger, shared by the workflow list, the new-workflow picker and the edit page so the three
	/// never disagree. A Workflows "Trigger.&lt;enum name&gt;" string names any trigger; failing that, the module that owns the trigger names
	/// it (Incident Back Office by "Trigger.&lt;enum name&gt;", the Business Ops, certification, inventory, work order and checklist modules by
	/// the bare enum name). A trigger with no name anywhere shows its enum name rather than a blank.
	/// </summary>
	public static class WorkflowTriggerNames
	{
		public static string For(IStringLocalizerFactory localizers, int trigger)
		{
			var name = ((WorkflowTriggerEventType)trigger).ToString();

			var workflows = localizers.Create(typeof(Strings.Workflows.Workflows))["Trigger." + name];
			if (!workflows.ResourceNotFound)
				return workflows.Value;

			var module = ModuleString(trigger);
			if (module.HasValue)
			{
				var named = localizers.Create(module.Value.Resource)[module.Value.Key];
				if (!named.ResourceNotFound)
					return named.Value;
			}

			return name;
		}

		/// <summary>The module strings and key that name a trigger outside the Workflows strings, or null when no module owns it.</summary>
		public static (Type Resource, string Key)? ModuleString(int trigger)
		{
			var name = ((WorkflowTriggerEventType)trigger).ToString();

			if (IncidentBackOfficeWorkflowPayload.IsIncident(trigger))
				return (typeof(Strings.IncidentBackOffice.IncidentBackOffice), "Trigger." + name);
			if (InvoiceWorkflowPayload.IsInvoice(trigger))
				return (typeof(Strings.Invoicing.Invoicing), name);
			if (DeploymentWorkflowPayload.IsDeployment(trigger))
				return (typeof(Strings.Deployments.Deployments), name);
			if (ContractorWorkflowPayload.IsContractor(trigger))
				return (typeof(Strings.ContractorBilling.ContractorBilling), name);
			if (CertificationWorkflowTriggers.IsCertification(trigger))
				return (typeof(Strings.Certifications.Certifications), name);
			if (InventoryWorkflowPayload.IsInventory(trigger))
				return (typeof(Strings.Inventory.Inventory), name);
			if (WorkOrderWorkflowPayload.IsWorkOrder(trigger))
				return (typeof(Strings.WorkOrders.WorkOrders), name);
			if (ChecklistWorkflowPayload.IsChecklist(trigger))
				return (typeof(Strings.Checklists.Checklists), name);

			return null;
		}
	}
}
