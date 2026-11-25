using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model.Lms
{
	/// <summary>
	/// Resgrid's competency framework catalog (§13 T3 item 3b, R-C7): the framework structure and the public identifiers a department
	/// needs, never a standard's text. Labels are position titles, qualification-level names and skill names only; requirement text
	/// stays with its publisher. NFPA 1010 / 1020 ship their qualification levels only: the 2024 consolidated editions' chapter and JPR
	/// numbers are added by the department (or imported through CASE) from the edition it trains to. A guard test keeps requirement
	/// prose out of these seeds.
	/// </summary>
	public static class LmsCompetencyFrameworkCatalog
	{
		public const string NfpaFirefighterKey = "nfpa-1010-levels";
		public const string NfpaOfficerInstructorKey = "nfpa-1020-levels";
		public const string NwcgPositionsKey = "nwcg-positions";
		public const string NremtEmtSkillsKey = "nremt-emt-skills";

		/// <summary>The longest label a catalog item may carry (a name, not a sentence).</summary>
		public const int MaxSeedLabelLength = 60;

		private static LmsCompetencyCatalogItem Level(string key, string label) =>
			new LmsCompetencyCatalogItem { Key = key, Label = label, ItemType = "Level" };

		private static LmsCompetencyCatalogItem Position(string code, string label) =>
			new LmsCompetencyCatalogItem { Key = code.ToLowerInvariant(), Code = code, Label = label, ItemType = "Position" };

		private static LmsCompetencyCatalogItem Skill(string key, string label) =>
			new LmsCompetencyCatalogItem { Key = key, Label = label, ItemType = "Skill" };

		public static readonly IReadOnlyList<LmsCompetencyCatalogFramework> All = new List<LmsCompetencyCatalogFramework>
		{
			new LmsCompetencyCatalogFramework
			{
				Key = NfpaFirefighterKey,
				Name = "NFPA 1010 qualification levels",
				Publisher = "NFPA (identifiers only)",
				Edition = "2024",
				Description = "Firefighter, driver/operator, airport and marine qualification levels. Add the chapter and JPR numbers of the edition you train to under each level.",
				Items = new List<LmsCompetencyCatalogItem>
				{
					Level("ff1", "Firefighter I"),
					Level("ff2", "Firefighter II"),
					Level("do-pumper", "Driver/Operator - Pumper"),
					Level("do-aerial", "Driver/Operator - Aerial"),
					Level("do-tiller", "Driver/Operator - Tiller"),
					Level("do-wildland", "Driver/Operator - Wildland Fire Apparatus"),
					Level("do-water-supply", "Driver/Operator - Mobile Water Supply"),
					Level("do-arff", "Driver/Operator - ARFF"),
					Level("airport-ff", "Airport Firefighter"),
					Level("marine-ff", "Land-Based Marine Firefighter")
				}
			},
			new LmsCompetencyCatalogFramework
			{
				Key = NfpaOfficerInstructorKey,
				Name = "NFPA 1020 qualification levels",
				Publisher = "NFPA (identifiers only)",
				Edition = "2024",
				Description = "Fire officer, instructor and EMS officer qualification levels. Add the chapter and JPR numbers of the edition you train to under each level.",
				Items = new List<LmsCompetencyCatalogItem>
				{
					Level("fo1", "Fire Officer I"),
					Level("fo2", "Fire Officer II"),
					Level("fo3", "Fire Officer III"),
					Level("fo4", "Fire Officer IV"),
					Level("fi1", "Fire and Emergency Services Instructor I"),
					Level("fi2", "Fire and Emergency Services Instructor II"),
					Level("fi3", "Fire and Emergency Services Instructor III"),
					Level("emso1", "EMS Officer I"),
					Level("emso2", "EMS Officer II"),
					Level("emso3", "EMS Officer III")
				}
			},
			new LmsCompetencyCatalogFramework
			{
				Key = NwcgPositionsKey,
				Name = "NWCG wildland positions",
				Publisher = "NWCG (position codes)",
				Edition = "PMS 310-1",
				Description = "Wildland fire position codes. Tag the courses and task-book tasks that lead to each position.",
				Items = new List<LmsCompetencyCatalogItem>
				{
					Position("FFT2", "Firefighter Type 2"),
					Position("FFT1", "Firefighter Type 1"),
					Position("ICT5", "Incident Commander Type 5"),
					Position("ICT4", "Incident Commander Type 4"),
					Position("ICT3", "Incident Commander Type 3"),
					Position("CRWB", "Crew Boss (Single Resource)"),
					Position("ENGB", "Engine Boss (Single Resource)"),
					Position("STEN", "Strike Team Leader Engine"),
					Position("STCR", "Strike Team Leader Crew"),
					Position("TFLD", "Task Force Leader"),
					Position("DIVS", "Division/Group Supervisor"),
					Position("FIRB", "Firing Boss"),
					Position("FEMO", "Fire Effects Monitor"),
					Position("HECM", "Helicopter Crewmember"),
					Position("FAL3", "Faller Type 3"),
					Position("FAL2", "Faller Type 2"),
					Position("FAL1", "Faller Type 1")
				}
			},
			new LmsCompetencyCatalogFramework
			{
				Key = NremtEmtSkillsKey,
				Name = "NREMT EMT psychomotor skills",
				Publisher = "NREMT (skill names)",
				Edition = "EMT",
				Description = "EMT psychomotor skill names. Tag the skill sheets, courses and trainings that evidence each skill.",
				Items = new List<LmsCompetencyCatalogItem>
				{
					Skill("pa-trauma", "Patient Assessment/Management - Trauma"),
					Skill("pa-medical", "Patient Assessment/Management - Medical"),
					Skill("bvm", "BVM Ventilation of an Apneic Adult Patient"),
					Skill("oxygen-nrb", "Oxygen Administration by Non-Rebreather Mask"),
					Skill("cardiac-arrest-aed", "Cardiac Arrest Management/AED"),
					Skill("bleeding-shock", "Bleeding Control/Shock Management"),
					Skill("spinal-supine", "Spinal Immobilization (Supine Patient)"),
					Skill("spinal-seated", "Spinal Immobilization (Seated Patient)"),
					Skill("long-bone", "Long Bone Immobilization"),
					Skill("joint", "Joint Immobilization")
				}
			}
		};

		public static LmsCompetencyCatalogFramework Find(string key) =>
			All.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase));
	}
}
