using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Resgrid.Model.Checklists
{
	public static class ChecklistTemplateCatalog
	{
		public const string Guidance = "Adapt these starting points to your equipment, manufacturer procedures and local requirements before use.";
		public static IReadOnlyList<ChecklistTemplate> All { get; } = BuildAll().AsReadOnly();

		public static ChecklistTemplate GetById(string id) => All.FirstOrDefault(t =>
			string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));

		public static IReadOnlyList<ChecklistTemplate> Search(string query)
		{
			if (string.IsNullOrWhiteSpace(query))
				return All;
			var terms = query.ToLowerInvariant().Split(new[] { ' ', ',', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
			return All.Where(t => terms.All(term => t.SearchText.Contains(term))).ToList().AsReadOnly();
		}

		public static IReadOnlyList<ChecklistTemplate> GetByCategory(ChecklistCategory category) =>
			All.Where(t => t.SuggestedCategory == category).ToList().AsReadOnly();

		private static (string Key, string Name, bool Critical) I(string key, string name) => (key, name, false);
		private static (string Key, string Name, bool Critical) C(string key, string name) => (key, name, true);

		// Stable catalog identities survive reordering. These are identifiers, not security tokens.
		private static string Id(string key) => new Guid(SHA256.HashData(Encoding.UTF8.GetBytes("resgrid:checklist:" + key)).Take(16).ToArray()).ToString();

		private static ChecklistTemplate T(string id, string name, string sector, string description,
			ChecklistCategory category, ChecklistScheduleFrequency frequency, ChecklistTargetType target,
			params (string Key, string Name, bool Critical)[] items)
			=> T(id, name, sector, description, category, frequency, target, false, items);

		private static ChecklistTemplate T(string id, string name, string sector, string description,
			ChecklistCategory category, ChecklistScheduleFrequency frequency, ChecklistTargetType target,
			bool requiresIndependentWitness, params (string Key, string Name, bool Critical)[] items)
		{
			var checks = items.Select(i => new ChecklistTemplateItem(Id(id + ":" + i.Key), i.Name,
				ChecklistItemType.PassFail, true, i.Critical, !i.Critical, true));
			return new ChecklistTemplate(id, name, sector, description, category, frequency, target,
				requiresIndependentWitness, new[] { category.ToString(), target.ToString() }, new[]
				{
					new ChecklistTemplateSection(Id(id + ":inspection"), "Readiness checks", checks),
					new ChecklistTemplateSection(Id(id + ":handover"), "Findings and handover", new[]
					{
						new ChecklistTemplateItem(Id(id + ":notes"), "Findings, restrictions and handover notes",
							ChecklistItemType.FreeText, false, false, false, false)
					})
				});
		}

		private static List<ChecklistTemplate> BuildAll() => new List<ChecklistTemplate>
		{
			T("fire-apparatus-daily", "Apparatus Daily Check", "Fire", "Start-of-shift readiness for an engine or truck.",
				ChecklistCategory.UnitCheck, ChecklistScheduleFrequency.Daily, ChecklistTargetType.Unit,
				C("brakes", "Braking and steering checks meet the approved procedure"), C("tires", "Tires, wheels and visible leaks checked"),
				I("warning", "Warning lights and audible devices checked"), I("communications", "Radios and communication equipment checked"),
				C("equipment", "Required equipment secured and ready"), I("fluids", "Fuel and fluid levels checked")),
			T("fire-apparatus-weekly", "Apparatus Weekly Check", "Fire", "Expanded checks alongside the daily apparatus procedure.",
				ChecklistCategory.UnitCheck, ChecklistScheduleFrequency.Weekly, ChecklistTargetType.Unit,
				C("pump", "Pump and related systems checked using the approved procedure"), C("ladders", "Ladders and mounting restraints inspected"),
				I("tools", "Powered tools and auxiliary equipment checked"), I("battery", "Battery and charging systems checked"),
				I("documents", "Service dates and unresolved defects reviewed")),
			T("fire-scba", "SCBA Inspection", "Fire", "Equipment-specific inspection using manufacturer instructions.",
				ChecklistCategory.EquipmentCheck, ChecklistScheduleFrequency.PerShift, ChecklistTargetType.InventoryAsset,
				C("cylinder", "Cylinder condition, pressure and service dates checked"), C("facepiece", "Facepiece and harness inspected"),
				C("regulator", "Regulator and connections checked"), C("alarms", "Alarms checked using the approved procedure"),
				I("clean", "Cleaning and storage condition checked")),
			T("fire-ppe", "PPE Routine Inspection", "Fire", "Personal protective equipment condition and retirement review.",
				ChecklistCategory.PersonalGear, ChecklistScheduleFrequency.PerShift, ChecklistTargetType.Personnel,
				C("damage", "Protective ensemble inspected for damage"), C("contamination", "Contamination and cleaning status checked"),
				I("closures", "Closures, seams and accessories inspected"), I("dates", "Inspection and retirement dates reviewed")),
			T("wildland-engine", "Wildland Engine Pre-Use", "Wildland / Contractors", "Agency deployment readiness; adapt to the agency's current check-in requirements.",
				ChecklistCategory.UnitCheck, ChecklistScheduleFrequency.OnDemand, ChecklistTargetType.Unit,
				C("vehicle", "Vehicle pre-use inspection completed"), C("pump", "Pump, hose and water systems checked"),
				I("equipment", "Required tools and equipment inventoried"), I("communications", "Incident communications checked"),
				I("documents", "Assignment and equipment documentation ready")),
			T("wildland-tender", "Water Tender Pre-Use", "Wildland / Contractors", "Water tender deployment and delivery readiness.",
				ChecklistCategory.UnitCheck, ChecklistScheduleFrequency.OnDemand, ChecklistTargetType.Unit,
				C("vehicle", "Vehicle and load safety checked"), C("tank", "Tank, valves and discharge systems inspected"),
				I("fill", "Fill fittings and adapters present"), I("communications", "Communications and deployment documents ready")),
			T("ems-ambulance", "Ambulance Daily Rig Check", "EMS", "Vehicle, patient compartment and response equipment readiness; no patient data.",
				ChecklistCategory.UnitCheck, ChecklistScheduleFrequency.Daily, ChecklistTargetType.Unit,
				C("vehicle", "Vehicle pre-use inspection completed"), C("restraints", "Stretcher, mounts and restraints checked"),
				C("devices", "Required clinical devices checked to manufacturer instructions"), I("stock", "Supplies and expiry dates checked"),
				I("clean", "Cleaning and infection-control supplies checked")),
			T("ems-jump-bag", "EMS Jump Bag Check", "EMS", "Compare contents to the department-approved stock list and par levels.",
				ChecklistCategory.EquipmentCheck, ChecklistScheduleFrequency.PerShift, ChecklistTargetType.InventoryAsset,
				I("seal", "Bag, seals and inventory identity checked"), C("stock", "Required stock meets local par levels"),
				C("expiry", "Expiry dates and packaging checked"), I("restock", "Shortages recorded and reported")),
			T("ems-controlled-count", "Controlled Supply Count Review", "EMS", "Requires two independently authenticated witnesses and the department's controlled-substance protocol before execution.",
				ChecklistCategory.SafetyAudit, ChecklistScheduleFrequency.PerShift, ChecklistTargetType.Unit, requiresIndependentWitness: true,
				C("security", "Storage security and seals checked"), C("count", "Count reconciled against the authorized ledger"),
				C("discrepancy", "Discrepancies escalated under the approved protocol"), I("expiry", "Expiry and storage conditions checked")),
			T("ems-aed", "AED Readiness Check", "EMS", "External readiness check; follow the device-specific instructions and interval.",
				ChecklistCategory.EquipmentCheck, ChecklistScheduleFrequency.Weekly, ChecklistTargetType.InventoryAsset,
				C("indicator", "Device readiness indicator checked"), C("consumables", "Pads, battery and expiry dates checked"),
				I("access", "Device location, access and accessories checked"), I("service", "Outstanding service notices reviewed")),
			T("sar-rope-cache", "SAR Rope and Equipment Cache", "SAR", "Cache identity, condition and service-life review.",
				ChecklistCategory.EquipmentCheck, ChecklistScheduleFrequency.OnDemand, ChecklistTargetType.Group,
				C("rope", "Ropes and textiles inspected under the approved procedure"), C("hardware", "Hardware condition and function inspected"),
				I("inventory", "Inventory and equipment identifiers reconciled"), C("retirement", "Quarantine and retirement limits reviewed")),
			T("sar-personal-pack", "Personal 24-Hour Pack", "SAR", "Adapt pack contents to the mission, season and local requirements.",
				ChecklistCategory.PersonalGear, ChecklistScheduleFrequency.OnDemand, ChecklistTargetType.Personnel,
				I("supplies", "Mission supplies and personal provisions checked"), C("communications", "Communications and navigation equipment checked"),
				I("environment", "Clothing and shelter appropriate to conditions"), I("lighting", "Lighting and spare power checked")),
			T("post-deployment", "Post-Deployment Rehabilitation", "Emergency Management", "Equipment return, decontamination, replenishment and defect handover.",
				ChecklistCategory.EquipmentCheck, ChecklistScheduleFrequency.OnDemand, ChecklistTargetType.Unit,
				I("inventory", "Returned and missing equipment reconciled"), C("contamination", "Contaminated or damaged equipment isolated"),
				I("replenish", "Consumables and power replenished"), C("release", "Restrictions and required inspections handed over")),
			T("station-daily", "Station Daily Walkthrough", "Facilities", "Station access, housekeeping and shared facilities.",
				ChecklistCategory.Facility, ChecklistScheduleFrequency.Daily, ChecklistTargetType.Group,
				C("exits", "Emergency exits and access routes clear"), I("housekeeping", "Work and living areas checked"),
				I("utilities", "Utilities and visible leaks checked"), I("security", "Security and shared equipment checked")),
			T("station-monthly", "Facility Monthly Safety Review", "Facilities", "Site safety systems and overdue service review.",
				ChecklistCategory.SafetyAudit, ChecklistScheduleFrequency.Monthly, ChecklistTargetType.Group,
				C("egress", "Egress routes and emergency lighting checked"), I("fire", "Fire protection equipment service status reviewed"),
				I("electrical", "Visible electrical and storage hazards checked"), I("actions", "Prior findings and corrective actions reviewed")),
			T("personnel-shift", "Start-of-Shift Readiness", "Personnel", "Role, communications, assigned equipment and handover.",
				ChecklistCategory.StartOfShift, ChecklistScheduleFrequency.PerShift, ChecklistTargetType.Personnel,
				I("assignment", "Assignment and role acknowledged"), I("handover", "Previous shift handover reviewed"),
				C("equipment", "Required personal equipment ready"), I("communications", "Communications checked")),
			T("personnel-annual", "Annual Member Readiness Review", "Personnel", "Review qualification and equipment records in their systems of record.",
				ChecklistCategory.AnnualReview, ChecklistScheduleFrequency.Annual, ChecklistTargetType.Personnel,
				I("qualification", "Role qualifications and training status reviewed"), I("equipment", "Issued equipment reconciled"),
				I("contact", "Contact and emergency information reviewed"), I("actions", "Required follow-up assigned")),
			T("equipment-generator", "Pump / Generator Weekly Check", "Equipment", "Use the manufacturer's safe test procedure and recording limits.",
				ChecklistCategory.EquipmentCheck, ChecklistScheduleFrequency.Weekly, ChecklistTargetType.InventoryAsset,
				C("condition", "Damage, leaks and guards inspected"), C("operation", "Approved operational check completed"),
				I("readings", "Required run-hour and performance readings recorded in the equipment log"), I("service", "Service interval and fuel status checked")),
			T("equipment-chainsaw", "Chainsaw Monthly Inspection", "Equipment", "Inspection and service readiness using manufacturer procedures.",
				ChecklistCategory.EquipmentCheck, ChecklistScheduleFrequency.Monthly, ChecklistTargetType.InventoryAsset,
				C("safety", "Safety devices and guards checked"), C("cutting", "Cutting assembly condition inspected"),
				I("fluids", "Fuel, lubrication and leaks checked"), I("storage", "Storage and service dates reviewed")),
			T("industrial-safety", "Workplace Safety Walkthrough", "Industry / Business", "Adapt hazards and inspection scope to the workplace.",
				ChecklistCategory.SafetyAudit, ChecklistScheduleFrequency.Weekly, ChecklistTargetType.Group,
				C("egress", "Emergency access and exits clear"), C("guards", "Required machine guards and barriers in place"),
				I("storage", "Materials and chemical storage inspected"), I("housekeeping", "Trip and housekeeping hazards checked"),
				I("actions", "Worker-reported hazards and previous findings reviewed")),
			T("equipment-extinguisher", "Fire Extinguisher Visual Check", "Facilities", "Visual readiness and service-label review; adapt the interval to local requirements.",
				ChecklistCategory.EquipmentCheck, ChecklistScheduleFrequency.Monthly, ChecklistTargetType.InventoryAsset,
				C("access", "Extinguisher accessible in its designated location"), C("condition", "Visible condition and readiness indicator checked"),
				I("seal", "Seal and instructions inspected"), I("service", "Inspection and service dates reviewed")),
			T("industrial-forklift", "Forklift Pre-Operation", "Industry", "Pre-use inspection; use each shift for continuous operations and follow the approved procedure.",
				ChecklistCategory.EquipmentCheck, ChecklistScheduleFrequency.PerShift, ChecklistTargetType.InventoryAsset,
				C("vehicle", "Tires, forks, mast and visible damage inspected"), C("leaks", "Fluid leaks and energy source condition checked"),
				C("controls", "Brakes, steering and safety controls checked"), C("defects", "Unsafe defects reported and equipment withheld from use")),
			T("business-vehicle", "Vehicle Pre-Trip", "Business / Fleet", "Vehicle-specific inspection and documentation before a trip.",
				ChecklistCategory.UnitCheck, ChecklistScheduleFrequency.OnDemand, ChecklistTargetType.Unit,
				C("brakes", "Braking, steering and tires checked"), C("load", "Load and equipment restraints checked"),
				I("lighting", "Lights and visibility checked"), I("documents", "Required documents and prior defects reviewed")),
			T("em-eoc", "EOC Activation and Handover", "Emergency Management", "Operational-period readiness for an emergency operations center.",
				ChecklistCategory.StartOfShift, ChecklistScheduleFrequency.OnDemand, ChecklistTargetType.Group,
				I("roles", "Operational roles and contact roster confirmed"), C("communications", "Primary and backup communications checked"),
				I("systems", "Situation displays and information systems available"), I("handover", "Objectives, open actions and handover recorded"),
				I("power", "Power and facility support arrangements checked")),
			T("em-shelter", "Shelter Opening Readiness", "Emergency Management", "Facility, accessibility, staffing and support service readiness.",
				ChecklistCategory.Facility, ChecklistScheduleFrequency.OnDemand, ChecklistTargetType.Group,
				C("facility", "Facility access, exits and hazards checked"), C("accessibility", "Accessibility and support arrangements checked"),
				I("supplies", "Supplies, sanitation and communications available"), I("roles", "Staffing, referrals and escalation contacts confirmed")),
			T("em-cache", "Emergency Cache Deployment / Return", "Emergency Management", "Resource condition and accountability before deployment and on return.",
				ChecklistCategory.EquipmentCheck, ChecklistScheduleFrequency.OnDemand, ChecklistTargetType.Group,
				I("inventory", "Resource inventory and custody reconciled"), C("condition", "Equipment condition and service status checked"),
				I("expiry", "Consumable expiry and stock levels reviewed"), I("routing", "Destination, custodian and missing resources recorded")),
			T("business-opening", "Business Opening / Closing", "Business", "Site access, safety and handover for routine operations.",
				ChecklistCategory.Facility, ChecklistScheduleFrequency.Daily, ChecklistTargetType.Group,
				C("access", "Site security and emergency access checked"), I("utilities", "Utilities and operating equipment checked"),
				I("staffing", "Coverage and escalation contacts confirmed"), I("handover", "Open issues and closing handover recorded")),
			T("business-continuity", "Continuity Readiness Review", "Business / Emergency Management", "Exercise communications and continuity arrangements on the organization's chosen cycle.",
				ChecklistCategory.SafetyAudit, ChecklistScheduleFrequency.Quarterly, ChecklistTargetType.Department,
				I("contacts", "Emergency contacts and notification process reviewed"), C("alternates", "Alternate operating and communications arrangements checked"),
				I("suppliers", "Critical supplier and service dependencies reviewed"), I("exercise", "Exercise findings and corrective actions assigned"))
		};
	}
}
