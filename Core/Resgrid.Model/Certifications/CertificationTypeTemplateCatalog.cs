using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model.Certifications
{
	/// <summary>
	/// Code-defined seed templates for department certification types (Workforce &amp; Business Operations plan,
	/// Phase D1.2; structural clone of <see cref="Resgrid.Model.UnitRoles.UnitRoleTemplateCatalog"/>). Nothing here
	/// lives in the database: "add to department" projects a template into an unsaved
	/// <see cref="DepartmentCertificationType"/> the department then owns and may edit. Validity months are the
	/// common renewal cycle for the credential; the department can override them per type. Codes are stable and
	/// unique across the catalog so a department that adds the same template twice gets a duplicate-code refusal.
	/// </summary>
	public static class CertificationTypeTemplateCatalog
	{
		public static IReadOnlyList<CertificationTypeTemplate> All { get; } = BuildAll();

		public static CertificationTypeTemplate GetById(string id)
		{
			if (string.IsNullOrWhiteSpace(id))
				return null;

			return All.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));
		}

		public static IReadOnlyList<CertificationTypeTemplate> GetByCategory(CertificationCategories category)
			=> All.Where(t => t.Category == category).ToList();

		public static IReadOnlyList<CertificationTypeTemplate> GetByScope(CertificationAppliesTo appliesTo)
			=> All.Where(t => t.AppliesTo == appliesTo).ToList();

		/// <summary>Free-text filter over code, name, category, authority, description and keywords (every term must match).</summary>
		public static IReadOnlyList<CertificationTypeTemplate> Search(string query)
		{
			if (string.IsNullOrWhiteSpace(query))
				return All;

			var terms = query.ToLowerInvariant().Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
			return All.Where(t => terms.All(term => SearchText(t).Contains(term))).ToList();
		}

		/// <summary>Projects a template into an unsaved department type (plan D1.2 "add to department").</summary>
		public static DepartmentCertificationType ToDepartmentType(CertificationTypeTemplate template, int departmentId)
		{
			if (template == null)
				return null;

			return new DepartmentCertificationType
			{
				DepartmentId = departmentId,
				Type = template.Name,
				Code = template.Code,
				Category = (int)template.Category,
				AppliesTo = (int)template.AppliesTo,
				Description = template.Description,
				IssuingAuthority = template.IssuingAuthority,
				DefaultValidityMonths = template.NeverExpires ? null : template.DefaultValidityMonths,
				NeverExpires = template.NeverExpires,
				RequiresVerification = template.RequiresVerification,
				RenewalCreditHoursRequired = template.RenewalCreditHoursRequired,
				IsActive = true
			};
		}

		private static string SearchText(CertificationTypeTemplate t)
			=> string.Join(" ", new[] { t.Code, t.Name, t.Category.ToString(), t.AppliesTo.ToString(), t.IssuingAuthority, t.Description }
				.Concat(t.Keywords ?? Array.Empty<string>()).Where(s => !string.IsNullOrWhiteSpace(s))).ToLowerInvariant();

		#region Builders

		private static CertificationTypeTemplate P(string code, string name, CertificationCategories category, string authority, int? months, string description, params string[] keywords)
			=> new CertificationTypeTemplate { Id = code.ToLowerInvariant(), Code = code, Name = name, Category = category, AppliesTo = CertificationAppliesTo.Person, IssuingAuthority = authority, DefaultValidityMonths = months, NeverExpires = months == null, Description = description, Keywords = keywords };

		private static CertificationTypeTemplate U(string code, string name, CertificationCategories category, string authority, int? months, string description, params string[] keywords)
			=> new CertificationTypeTemplate { Id = code.ToLowerInvariant(), Code = code, Name = name, Category = category, AppliesTo = CertificationAppliesTo.Unit, IssuingAuthority = authority, DefaultValidityMonths = months, NeverExpires = months == null, Description = description, Keywords = keywords };

		private static CertificationTypeTemplate Verified(this CertificationTypeTemplate t) { t.RequiresVerification = true; return t; }
		private static CertificationTypeTemplate Credits(this CertificationTypeTemplate t, decimal hours) { t.RenewalCreditHoursRequired = hours; return t; }

		private static IReadOnlyList<CertificationTypeTemplate> BuildAll()
		{
			const CertificationCategories Fire = CertificationCategories.Fire, EMS = CertificationCategories.EMS, SAR = CertificationCategories.SAR,
				Wild = CertificationCategories.Wildland, EM = CertificationCategories.EmergencyManagement, Ind = CertificationCategories.Industrial,
				Sec = CertificationCategories.Security, Med = CertificationCategories.Medical, Drv = CertificationCategories.Driver, Veh = CertificationCategories.Vehicle;

			return new List<CertificationTypeTemplate>
			{
				// ---- Fire (NFPA ladder; Pro Board / IFSAC accredited) --------------------------------------
				P("FF1", "Firefighter I", Fire, "NFPA 1001 (Pro Board / IFSAC)", null, "Entry-level structural firefighter certification.", "nfpa", "structural"),
				P("FF2", "Firefighter II", Fire, "NFPA 1001 (Pro Board / IFSAC)", null, "Advanced structural firefighter certification.", "nfpa", "structural"),
				P("DO-PUMP", "Driver/Operator – Pumper", Fire, "NFPA 1002 (Pro Board / IFSAC)", null, "Apparatus driver/operator for pumping apparatus.", "nfpa", "apparatus", "engine"),
				P("DO-AERIAL", "Driver/Operator – Aerial", Fire, "NFPA 1002 (Pro Board / IFSAC)", null, "Apparatus driver/operator for aerial apparatus.", "nfpa", "apparatus", "ladder", "truck"),
				P("HAZMAT-OPS", "Hazardous Materials Operations", Fire, "NFPA 470", 12, "Hazmat operations level; annual refresher under OSHA 1910.120(q).", "hazmat", "osha"),
				P("HAZMAT-TECH", "Hazardous Materials Technician", Fire, "NFPA 470", 12, "Hazmat technician level; annual refresher.", "hazmat", "osha"),
				P("FO1", "Fire Officer I", Fire, "NFPA 1021 (Pro Board / IFSAC)", null, "Company officer certification.", "nfpa", "officer"),
				P("FO2", "Fire Officer II", Fire, "NFPA 1021 (Pro Board / IFSAC)", null, "Fire officer II certification.", "nfpa", "officer"),
				P("FI1", "Fire Instructor I", Fire, "NFPA 1041 (Pro Board / IFSAC)", null, "Fire service instructor I.", "nfpa", "instructor"),
				P("FINSP1", "Fire Inspector I", Fire, "NFPA 1031 (Pro Board / IFSAC)", null, "Fire inspector I.", "nfpa", "inspector", "prevention"),
				P("FIRE-CONED", "Fire Continuing Education", Fire, "State fire marshal / training authority", 24, "Recertification con-ed cycle for state fire certifications.", "recert", "hours").Credits(24m),

				// ---- EMS (national registry AND state license are separate credentials) ---------------------
				P("NREMT-EMR", "NREMT Emergency Medical Responder", EMS, "National Registry of EMTs", 24, "National registry EMR certification (2-year cycle).", "nremt", "emr").Credits(16m),
				P("NREMT-EMT", "NREMT Emergency Medical Technician", EMS, "National Registry of EMTs", 24, "National registry EMT certification (2-year cycle, NCCP).", "nremt", "emt").Credits(40m),
				P("NREMT-AEMT", "NREMT Advanced EMT", EMS, "National Registry of EMTs", 24, "National registry AEMT certification (2-year cycle, NCCP).", "nremt", "aemt").Credits(50m),
				P("NREMT-P", "NREMT Paramedic", EMS, "National Registry of EMTs", 24, "National registry paramedic certification (2-year cycle, NCCP).", "nremt", "paramedic", "medic").Credits(60m),
				P("STATE-EMT", "State EMT License", EMS, "State EMS office", 24, "State EMT license or certification; number and expiry differ from the national registry card.", "license", "state", "emt"),
				P("STATE-AEMT", "State Advanced EMT License", EMS, "State EMS office", 24, "State AEMT license.", "license", "state", "aemt"),
				P("STATE-P", "State Paramedic License", EMS, "State EMS office", 24, "State paramedic license.", "license", "state", "paramedic"),
				P("BLS", "BLS Provider (CPR/AED)", Med, "American Heart Association / Red Cross", 24, "Basic Life Support card (eCard number).", "cpr", "aed", "aha"),
				P("ACLS", "ACLS Provider", Med, "American Heart Association", 24, "Advanced Cardiovascular Life Support card.", "aha", "cardiac"),
				P("PALS", "PALS Provider", Med, "American Heart Association", 24, "Pediatric Advanced Life Support card.", "aha", "pediatric"),
				P("PHTLS", "PHTLS Provider", Med, "NAEMT", 48, "Prehospital Trauma Life Support card (4-year cycle).", "naemt", "trauma"),
				P("MD-SKILLS", "Medical Director Skills Verification", EMS, "Agency medical director", 12, "Annual medical-director skill verification sign-off.", "medical director", "skills").Verified(),

				// ---- SAR --------------------------------------------------------------------------------------
				P("SARTECH3", "SARTECH III", SAR, "NASAR", null, "NASAR SARTECH III.", "nasar", "search"),
				P("SARTECH2", "SARTECH II", SAR, "NASAR", 36, "NASAR SARTECH II (3-year recert).", "nasar", "search"),
				P("SARTECH1", "SARTECH I", SAR, "NASAR", 36, "NASAR SARTECH I (3-year recert).", "nasar", "search"),
				P("ROPE-TECH", "Rope Rescue Technician", SAR, "NFPA 1006 / agency", 36, "Technical rope rescue technician.", "rope", "rescue", "technical"),
				P("SWIFTWATER", "Swiftwater Rescue Technician", SAR, "NFPA 1006 / agency", 36, "Swiftwater / flood rescue technician.", "water", "flood", "rescue"),
				P("AVALANCHE", "Avalanche Rescue", SAR, "AIARE / agency", 36, "Avalanche rescue and companion rescue certification.", "snow", "rescue"),
				P("WFR", "Wilderness First Responder", Med, "WMA / NOLS / SOLO", 36, "Wilderness First Responder (3-year cycle).", "wilderness", "medical"),
				P("WFA", "Wilderness First Aid", Med, "WMA / NOLS / SOLO / Red Cross", 24, "Wilderness First Aid (2-year cycle).", "wilderness", "first aid"),
				P("K9-TEAM", "K9 Search Team Certification", SAR, "NASAR / state K9 standard", 24, "Handler-and-dog team certification (air scent, trailing, HRD).", "canine", "dog", "handler"),
				P("HELO-OPS", "Helicopter Operations", SAR, "Agency / hoist program", 12, "Helicopter operations and hoist safety currency.", "aviation", "hoist"),

				// ---- Wildland (NWCG) --------------------------------------------------------------------------
				P("NWCG-FFT2", "NWCG Firefighter Type 2 (FFT2)", Wild, "NWCG", 60, "Entry-level wildland firefighter position qualification (currency 5 years).", "nwcg", "red card", "wildland"),
				P("NWCG-FFT1", "NWCG Firefighter Type 1 (FFT1)", Wild, "NWCG", 60, "Squad boss position qualification.", "nwcg", "wildland"),
				P("NWCG-CRWB", "NWCG Crew Boss (CRWB)", Wild, "NWCG", 60, "Crew boss position qualification.", "nwcg", "wildland"),
				P("NWCG-ENGB", "NWCG Engine Boss (ENGB)", Wild, "NWCG", 60, "Engine boss position qualification.", "nwcg", "wildland"),
				P("NWCG-DIVS", "NWCG Division/Group Supervisor (DIVS)", Wild, "NWCG", 60, "Division/group supervisor position qualification.", "nwcg", "wildland"),
				P("RED-CARD", "Incident Qualification Card (Red Card)", Wild, "NWCG / agency", 12, "Annual incident qualification card; carries the WCT fitness level (Arduous / Moderate / Light).", "nwcg", "wct", "fitness", "arduous"),
				P("RT-130", "RT-130 Annual Fireline Safety Refresher", Wild, "NWCG", 12, "Annual wildland fire safety refresher.", "nwcg", "refresher"),
				P("WCT", "Work Capacity Test", Wild, "NWCG", 12, "Annual pack test; record the level (Arduous / Moderate / Light) in the number field.", "pack test", "fitness"),

				// ---- Emergency management (FEMA courses never expire) ---------------------------------------
				P("ICS-100", "ICS-100 Introduction to ICS", EM, "FEMA EMI", null, "FEMA IS-100 / ICS-100 (does not expire).", "fema", "ics", "nims"),
				P("ICS-200", "ICS-200 Basic ICS", EM, "FEMA EMI", null, "FEMA IS-200 / ICS-200 (does not expire).", "fema", "ics", "nims"),
				P("ICS-300", "ICS-300 Intermediate ICS", EM, "FEMA EMI / state", null, "Intermediate ICS for expanding incidents (does not expire).", "fema", "ics", "nims"),
				P("ICS-400", "ICS-400 Advanced ICS", EM, "FEMA EMI / state", null, "Advanced ICS for command and general staff (does not expire).", "fema", "ics", "nims"),
				P("IS-700", "IS-700 NIMS Introduction", EM, "FEMA EMI", null, "FEMA IS-700 (does not expire).", "fema", "nims"),
				P("IS-800", "IS-800 National Response Framework", EM, "FEMA EMI", null, "FEMA IS-800 (does not expire).", "fema", "nrf"),
				P("CEM", "Certified Emergency Manager (CEM)", EM, "IAEM", 60, "IAEM CEM; 5-year recert with documented contact hours.", "iaem", "manager").Credits(100m),
				P("AEM", "Associate Emergency Manager (AEM)", EM, "IAEM", 60, "IAEM AEM; 5-year recert with documented contact hours.", "iaem", "manager").Credits(100m),

				// ---- Industrial / business --------------------------------------------------------------------
				P("OSHA-10", "OSHA 10-Hour", Ind, "OSHA Outreach", null, "OSHA 10-hour outreach card (no federal expiry; employer policy may impose one).", "osha", "safety"),
				P("OSHA-30", "OSHA 30-Hour", Ind, "OSHA Outreach", null, "OSHA 30-hour outreach card.", "osha", "safety"),
				P("HAZWOPER-40", "HAZWOPER 40-Hour", Ind, "OSHA 1910.120", 12, "HAZWOPER 40-hour initial; the 8-hour annual refresher is a separate type.", "osha", "hazwoper"),
				P("HAZWOPER-8", "HAZWOPER 8-Hour Annual Refresher", Ind, "OSHA 1910.120", 12, "Annual HAZWOPER refresher.", "osha", "hazwoper", "refresher"),
				P("FORKLIFT", "Powered Industrial Truck Operator", Ind, "OSHA 1910.178", 36, "Forklift operator evaluation (3-year re-evaluation).", "osha", "forklift", "pit"),
				P("CONFINED-SPACE", "Confined Space Entry", Ind, "OSHA 1910.146", 12, "Permit-required confined space entrant / attendant / supervisor.", "osha"),
				P("LOTO", "Lockout/Tagout Authorized Employee", Ind, "OSHA 1910.147", 12, "Lockout/tagout authorized employee training.", "osha"),
				P("RESP-FIT", "Respirator Fit Test", Ind, "OSHA 1910.134", 12, "Annual respirator fit test.", "osha", "scba", "respirator"),
				P("HEARING", "Hearing Conservation", Ind, "OSHA 1910.95", 12, "Annual audiometric test / hearing conservation training.", "osha"),
				P("FA-CPR-AED", "First Aid / CPR / AED", Med, "Red Cross / AHA / NSC", 24, "Workplace first aid, CPR and AED card.", "cpr", "aed", "first aid"),
				P("BBP", "Bloodborne Pathogens", Ind, "OSHA 1910.1030", 12, "Annual bloodborne pathogens training.", "osha"),
				P("TWIC", "Transportation Worker Identification Credential", Ind, "TSA", 60, "TWIC card (5-year).", "tsa", "port", "maritime"),
				P("NCCCO", "NCCCO Crane Operator", Ind, "NCCCO", 60, "Certified crane operator (5-year recert).", "crane", "operator"),

				// ---- Security ---------------------------------------------------------------------------------
				P("GUARD-CARD", "Security Guard Registration / License", Sec, "State licensing board (e.g. CA BSIS)", 24, "State guard card; annual con-ed hours tracked as credits.", "bsis", "license", "guard").Credits(8m),
				P("ARMED", "Armed Security Endorsement", Sec, "State licensing board", 24, "Exposed firearm permit / armed endorsement; requalification is a separate short-cycle type.", "firearm", "license"),
				P("FIREARM-REQUAL", "Firearm Requalification", Sec, "Agency range master / state", 6, "Quarterly or semi-annual firearm requalification.", "firearm", "range", "qualification").Verified(),
				P("BATON-OC", "Baton / OC Spray Permit", Sec, "State licensing board", 24, "Baton and chemical agent permits.", "baton", "oc", "pepper spray"),
				P("USE-OF-FORCE", "Use of Force Recertification", Sec, "Agency / state", 12, "Annual use-of-force recertification.", "force", "recert"),
				P("PI-LICENSE", "Private Investigator License", Sec, "State licensing board", 24, "Private investigator license.", "investigator", "license"),
				P("BC-SWL", "BC Security Worker Licence", Sec, "BC Security Programs Division", 24, "British Columbia security worker licence.", "canada", "bc", "licence"),

				// ---- Driver qualification file (49 CFR 391) -----------------------------------------------------
				P("CDL-A", "Commercial Driver's License – Class A", Drv, "State DMV (49 CFR 383)", 48, "Class A CDL; record endorsements as separate types.", "cdl", "dot", "fmcsa", "driver"),
				P("CDL-B", "Commercial Driver's License – Class B", Drv, "State DMV (49 CFR 383)", 48, "Class B CDL (straight trucks, buses).", "cdl", "dot", "fmcsa", "driver", "bus"),
				P("CDL-P", "CDL Passenger Endorsement (P)", Drv, "State DMV", 48, "Passenger endorsement; expiry follows the CDL.", "cdl", "endorsement", "bus", "shuttle"),
				P("CDL-S", "CDL School Bus Endorsement (S)", Drv, "State DMV", 48, "School bus endorsement.", "cdl", "endorsement", "school bus"),
				P("CDL-H", "CDL Hazmat Endorsement (H)", Drv, "State DMV + TSA STA", 60, "Hazardous materials endorsement with TSA security threat assessment (5-year).", "cdl", "endorsement", "hazmat", "tsa"),
				P("CDL-N", "CDL Tank Endorsement (N)", Drv, "State DMV", 48, "Tank vehicle endorsement.", "cdl", "endorsement", "tanker"),
				P("CDL-T", "CDL Doubles/Triples Endorsement (T)", Drv, "State DMV", 48, "Doubles/triples endorsement.", "cdl", "endorsement"),
				P("DOT-MED", "DOT Medical Examiner's Certificate", Drv, "FMCSA-registered medical examiner (49 CFR 391.43)", 24, "DOT medical card; maximum 24 months, often 12 when restricted. Separate expiry from the CDL.", "dot", "medical", "physical", "fmcsa"),
				P("MVR-REVIEW", "Annual Motor Vehicle Record Review", Drv, "Motor carrier (49 CFR 391.25)", 12, "Annual MVR review of each driver's record.", "dot", "mvr", "fmcsa"),
				P("CLEARINGHOUSE", "FMCSA Clearinghouse Annual Query", Drv, "Motor carrier (49 CFR 382.701)", 12, "Annual limited query of the Drug & Alcohol Clearinghouse.", "dot", "fmcsa", "drug", "alcohol"),
				P("ELDT", "Entry-Level Driver Training Completion", Drv, "FMCSA Training Provider Registry (49 CFR 380)", null, "ELDT completion (one-time; does not expire).", "dot", "fmcsa", "training"),
				P("ROAD-TEST", "Road Test Certificate", Drv, "Motor carrier (49 CFR 391.31)", null, "Road test certificate or equivalent (does not expire).", "dot", "fmcsa"),
				P("DA-PROGRAM", "Drug & Alcohol Program Enrollment", Drv, "Motor carrier / consortium (49 CFR 382)", 12, "Enrollment in a DOT drug and alcohol testing program.", "dot", "fmcsa", "testing"),
				P("SCHOOL-BUS-PERMIT", "State School Bus / Passenger Driver Permit", Drv, "State education / DMV", 12, "State school-bus or passenger driver permit.", "bus", "permit", "state"),

				// ---- Vehicle / apparatus (unit-scoped) -------------------------------------------------------------
				U("DOT-INSPECTION", "DOT Annual Vehicle Inspection", Veh, "Qualified inspector (49 CFR 396.17)", 12, "Annual periodic inspection of a commercial motor vehicle.", "dot", "fmcsa", "inspection", "vehicle"),
				U("VEH-REG", "Vehicle Registration", Veh, "State DMV", 12, "Vehicle registration renewal.", "registration", "plate", "vehicle"),
				U("VEH-INS", "Vehicle Insurance", Veh, "Insurer", 12, "Commercial auto insurance certificate.", "insurance", "vehicle"),
				U("IFTA", "IFTA Decal / License", Veh, "State base jurisdiction", 12, "International Fuel Tax Agreement decal.", "fuel tax", "vehicle"),
				U("AMB-PERMIT", "State Ambulance Permit", Veh, "State EMS office", 12, "Per-rig ambulance permit / inspection.", "ambulance", "ems", "permit"),
				U("NFPA-1911-PUMP", "NFPA 1911 Annual Pump Test", Veh, "Third-party tester / department", 12, "Annual fire pump service test.", "nfpa", "pump", "apparatus", "engine"),
				U("AERIAL-CERT", "Aerial Ladder Certification", Veh, "Third-party tester (NFPA 1911)", 12, "Annual aerial device inspection and load test.", "nfpa", "aerial", "ladder", "apparatus"),
				U("GROUND-LADDER", "Ground Ladder Test", Veh, "Third-party tester (NFPA 1932)", 12, "Annual ground ladder service test.", "nfpa", "ladder"),
				U("CRANE-INSPECTION", "Crane / Hoist Annual Inspection", Veh, "Qualified person (OSHA 1910.179 / 1926.1412)", 12, "Annual inspection of a crane or hoist modeled as a unit.", "osha", "crane", "inspection"),
				U("EQUIP-INSPECTION", "Equipment Annual Inspection", Veh, "Manufacturer / third party", 12, "Annual safety inspection certificate for a piece of equipment modeled as a unit.", "inspection", "equipment")
			};
		}

		#endregion
	}
}
