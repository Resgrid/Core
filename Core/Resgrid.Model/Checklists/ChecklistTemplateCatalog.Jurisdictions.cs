using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model.Checklists
{
	/// <summary>
	/// Industrial ERT G8 CA2: Canadian and Ontario workplace fire-protection and OHS checks, plus the generic post-hot-work fire
	/// watch (G3 FW0). Resgrid-authored and paraphrased; nothing is copied from a standard or regulation. Each template names its
	/// basis with the edition where known; "(article to confirm)" marks a citation the gap plan §7 still asks to check against the
	/// primary text. These stay out of the catalog until a Canadian fire-protection or OHS reviewer signs them off (decision
	/// ERT-D10): move a template into BuildAll() when it is approved. "Supports record keeping for", never "complies with".
	/// </summary>
	public static partial class ChecklistTemplateCatalog
	{
		private static ChecklistTemplate J(string id, string name, string sector, string description, string jurisdiction, string basis,
			ChecklistCategory category, ChecklistScheduleFrequency frequency, ChecklistTargetType target, params (string Key, string Name, bool Critical)[] items)
		{
			var checks = items.Select(i => new ChecklistTemplateItem(Id(id + ":" + i.Key), i.Name, ChecklistItemType.PassFail, true, i.Critical, !i.Critical, true));
			return new ChecklistTemplate(id, name, sector, description, category, frequency, target, false,
				new[] { category.ToString(), target.ToString(), jurisdiction ?? "general" }, new[]
				{
					new ChecklistTemplateSection(Id(id + ":inspection"), "Inspection", checks),
					new ChecklistTemplateSection(Id(id + ":handover"), "Findings and follow-up", new[]
					{
						new ChecklistTemplateItem(Id(id + ":notes"), "Deficiencies found, who was told and what was done", ChecklistItemType.FreeText, false, false, false, false)
					})
				}, jurisdiction, basis);
		}

		private static List<ChecklistTemplate> BuildPendingReview() => new List<ChecklistTemplate>
		{
			J("ca-on-extinguisher-monthly", "Portable Extinguisher Monthly Inspection (Ontario)", "Facilities / Industry",
				"Monthly visual inspection of each portable extinguisher. Annual maintenance, 6-year maintenance and hydrostatic tests are done by a qualified technician and tracked separately.",
				"CA-ON", "Ontario Fire Code (O. Reg. 213/07), Division B, Sentence 6.2.7.2 (monthly inspection); maintenance and testing to NFPA 10 (Sentence 6.2.7.1)",
				ChecklistCategory.EquipmentCheck, ChecklistScheduleFrequency.Monthly, ChecklistTargetType.InventoryAsset,
				C("present", "Extinguisher is in its designated place (not missing or moved)"), C("access", "Nothing blocks access to it or hides it from view"),
				C("charge", "Pressure gauge reads in the operable range, or the unit is the right weight"), C("seal", "Safety pin and tamper seal are in place"),
				C("damage", "No visible damage, corrosion, leaks or blocked nozzle"), I("label", "Operating instructions are legible and face outward"),
				I("tag", "Service tag shows a current annual maintenance"), I("signage", "Location sign is in place where one is required")),
			J("ca-on-jhsc-monthly", "JHSC Monthly Workplace Inspection (Ontario)", "Industry / Business",
				"The worker member's monthly inspection of part of the workplace, so that all of it is inspected at least once a year. Findings go to the joint health and safety committee.",
				"CA-ON", "Ontario Occupational Health and Safety Act, s. 9(23)–(28) (workplace inspections by a committee member)",
				ChecklistCategory.SafetyAudit, ChecklistScheduleFrequency.Monthly, ChecklistTargetType.Group,
				I("area", "The area inspected this month is recorded"), C("hazards", "Hazards seen are recorded with their location"),
				I("previous", "Findings from earlier inspections were followed up"), I("workers", "Workers in the area were asked about hazards"),
				I("committee", "Findings are ready to report to the committee")),
			J("ca-on-fire-drill", "Fire Drill Record (Ontario)", "Facilities / Industry",
				"Record of a fire drill run under the building's fire safety plan, including what supervisory staff did and what needs fixing.",
				"CA-ON", "Ontario Fire Code (O. Reg. 213/07), Division B, Section 2.8 (fire safety plan and fire drills; article to confirm)",
				ChecklistCategory.SafetyAudit, ChecklistScheduleFrequency.OnDemand, ChecklistTargetType.Department,
				I("notice", "Monitoring company and anyone affected were told before the drill"), C("alarm", "The fire alarm sounded throughout the drill area"),
				C("supervisory", "Supervisory staff carried out their duties in the fire safety plan"), C("evacuation", "Occupants left by the planned exits and were accounted for"),
				I("time", "Time to evacuate was recorded"), I("restore", "Alarm system was restored and the monitoring company told")),
			J("ca-on-fire-safety-plan-review", "Annual Fire Safety Plan Review (Ontario)", "Facilities / Industry",
				"Yearly review of the fire safety plan: people, procedures, drawings and the fire protection systems it relies on.",
				"CA-ON", "Ontario Fire Code (O. Reg. 213/07), Division B, Section 2.8 (fire safety plan; article to confirm)",
				ChecklistCategory.AnnualReview, ChecklistScheduleFrequency.Annual, ChecklistTargetType.Department,
				C("contacts", "Supervisory staff names and contacts are current"), C("procedures", "Alarm, evacuation and fire department notification procedures are current"),
				I("drawings", "Floor plans and system locations match the building"), I("impairments", "The procedure for fire protection systems out of service is current"),
				I("training", "Supervisory staff training is recorded"), I("approval", "Changes were sent to the fire department where approval is required")),
			J("ca-fire-alarm-monthly", "Fire Alarm System Monthly Check (Canada)", "Facilities / Industry",
				"Monthly check of the fire alarm system: panel condition, one initiating device in rotation, signals and standby power. The annual inspection is done by a qualified technician.",
				"CA", "CAN/ULC-S536 (inspection and testing of fire alarm systems; confirm the adopted edition), referenced by the Ontario Fire Code Division B Section 6.3 (article to confirm)",
				ChecklistCategory.Facility, ChecklistScheduleFrequency.Monthly, ChecklistTargetType.Group,
				I("notice", "Monitoring company was told before and after the test"), C("panel", "Control panel shows normal, with no trouble or supervisory signals"),
				C("device", "The initiating device due this month was tested and reached the panel"), C("signals", "Alarm signals sounded in the zone tested"),
				I("power", "Standby power and battery indicators are normal"), I("log", "Test is entered in the system log")),
			J("ca-emergency-lighting-monthly", "Emergency Lighting Monthly Check (Canada)", "Facilities / Industry",
				"Monthly test of emergency lights and exit signs on battery, with the longer annual duration test tracked separately.",
				"CA", "Ontario Fire Code (O. Reg. 213/07), Division B, Section 6.5 (emergency power and lighting; article to confirm); CSA C22.2 No. 141 (confirm edition)",
				ChecklistCategory.Facility, ChecklistScheduleFrequency.Monthly, ChecklistTargetType.Group,
				C("lights", "Emergency lights come on when switched to battery"), C("exits", "Exit signs are lit and visible"),
				I("aim", "Lamp heads point at exits and paths"), I("charge", "Charging indicators are normal after the test")),
			J("sprinkler-valve-check", "Sprinkler Control Valve and Fire Pump Check", "Facilities / Industry",
				"Routine check of sprinkler control valves, gauges and the fire pump room. Sealed valves are usually checked weekly and locked or supervised valves monthly; follow the adopted edition and your insurer.",
				null, "NFPA 25 (inspection, testing and maintenance of water-based fire protection systems; confirm the adopted edition)",
				ChecklistCategory.Facility, ChecklistScheduleFrequency.Weekly, ChecklistTargetType.Group,
				C("valves", "Control valves are open and sealed, locked or supervised"), C("gauges", "System gauges show normal water and air pressure"),
				I("fdc", "Fire department connection is visible, capped and clear"), I("pump", "Fire pump room is heated, clear and the pump controller shows normal"),
				I("heads", "No damaged, painted or blocked sprinkler heads seen")),
			J("post-hot-work-fire-watch", "Post-Hot-Work Fire Watch", "Industry / Facilities",
				"Interim fire watch after hot work, until the watch engine is available: one attestation per round, then the monitoring period the permit sets. This check has no timer and no missed-round alert; set reminders and follow the permit.",
				null, "NFPA 51B (2019 or later): fire watch during hot work and for at least 60 minutes after it ends, with further monitoring as the permit-authorizing individual requires (insurers often ask for up to 3 hours)",
				ChecklistCategory.SafetyAudit, ChecklistScheduleFrequency.OnDemand, ChecklistTargetType.Department,
				C("start", "Watch started when hot work ended (time noted on the permit)"), C("extinguisher", "Extinguisher or charged hose is at hand"),
				C("round1", "First round: no smoke, heat, glow or fire found"), C("round2", "Next round: no smoke, heat, glow or fire found"),
				C("round3", "Next round: no smoke, heat, glow or fire found"), C("round4", "Last round of the 60 minutes: no smoke, heat, glow or fire found"),
				I("monitoring", "Further monitoring period on the permit completed"), C("signoff", "Area left safe and the permit signed off by the watcher"))
		};
	}
}
