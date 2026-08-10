using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Model;

namespace Resgrid.Chatbot.Services
{
	/// <summary>The families of incident the assistant carries ICS guidance for.</summary>
	public enum IncidentPlaybookType
	{
		/// <summary>Applies to every incident; also the fallback when the call type can't be classified.</summary>
		General = 0,
		StructureFire = 1,
		Wildland = 2,
		VehicleAccident = 3,
		Ems = 4,
		MassCasualty = 5,
		HazMat = 6,
		NaturalDisaster = 7,
		SearchAndRescue = 8,
		TechnicalRescue = 9,
		WaterRescue = 10,
		ActiveThreat = 11
	}

	/// <summary>
	/// One incident family's ICS guidance: the tactical benchmarks an IC works toward, the "have you
	/// done this yet" checklist, the positions this incident type usually needs staffed, and the
	/// questions worth putting in front of the commander.
	/// </summary>
	public sealed class IncidentPlaybook
	{
		public IncidentPlaybookType Type { get; set; }

		public string DisplayName { get; set; }

		/// <summary>Lower-case terms matched against the call's type/name/nature to infer this playbook.</summary>
		public IReadOnlyList<string> Keywords { get; set; } = Array.Empty<string>();

		/// <summary>Tactical benchmarks — matched loosely against the board's objectives to report progress.</summary>
		public IReadOnlyList<string> Benchmarks { get; set; } = Array.Empty<string>();

		/// <summary>Doctrine checklist for this incident type, ordered roughly by when it matters.</summary>
		public IReadOnlyList<string> Checklist { get; set; } = Array.Empty<string>();

		/// <summary>ICS positions this incident type normally needs filled; drives the "what's unfilled" answer.</summary>
		public IReadOnlyList<IncidentRoleType> KeyRoles { get; set; } = Array.Empty<IncidentRoleType>();

		/// <summary>Questions surfaced to the commander as one-tap prompts on the command board.</summary>
		public IReadOnlyList<string> SuggestedQuestions { get; set; } = Array.Empty<string>();
	}

	/// <summary>
	/// Static NIMS/ICS knowledge the incident assistant reasons with. Deliberately a code table rather
	/// than configuration: it must be available with no database round-trip and no per-department setup,
	/// and it is mirrored verbatim in the Resgrid IC app (<c>src/services/incident-assistant/ics-playbooks.ts</c>)
	/// so the same answers are available on-device when the app is offline. Keep the two in sync.
	///
	/// Nothing here is department policy — it is the common doctrine an IC is trained against. Answers
	/// built from it are always framed as prompts to the commander, never as orders.
	/// </summary>
	public static class IcsPlaybooks
	{
		private static readonly IncidentPlaybook GeneralPlaybook = new IncidentPlaybook
		{
			Type = IncidentPlaybookType.General,
			DisplayName = "Incident",
			Keywords = new[] { "incident" },
			Benchmarks = new[]
			{
				"Command established",
				"Initial size-up transmitted",
				"Incident action plan set",
				"Accountability in place",
				"Incident under control"
			},
			Checklist = new[]
			{
				"Command established, announced, and passed to dispatch",
				"Command post location set and shared with incoming resources",
				"Initial size-up / CAN report (Conditions, Actions, Needs) transmitted",
				"Incident action plan recorded on the board",
				"Safety Officer assigned once the incident is working",
				"Accountability (PAR) timer running",
				"Staging designated and a Staging Area Manager assigned as resources build",
				"Span of control kept to 3-7 resources per supervisor",
				"Rehab established for extended operations",
				"Operational period and transfer-of-command plan set for a long incident"
			},
			KeyRoles = new[] { IncidentRoleType.IncidentCommander, IncidentRoleType.SafetyOfficer },
			SuggestedQuestions = new[]
			{
				"PAR",
				"Incident status",
				"What objectives are still open?",
				"Span of control",
				"What am I missing?"
			}
		};

		private static readonly List<IncidentPlaybook> Playbooks = new List<IncidentPlaybook>
		{
			new IncidentPlaybook
			{
				Type = IncidentPlaybookType.StructureFire,
				DisplayName = "Structure fire",
				Keywords = new[]
				{
					"structure fire", "house fire", "building fire", "residential fire", "commercial fire",
					"apartment fire", "working fire", "room and contents", "chimney fire", "attic fire",
					"basement fire", "smoke in the structure", "fire alarm", "structure"
				},
				Benchmarks = new[]
				{
					"360 complete",
					"Water supply established",
					"Primary search all clear",
					"Fire under control",
					"Secondary search all clear",
					"Utilities secured",
					"Loss stopped",
					"Overhaul complete"
				},
				Checklist = new[]
				{
					"360 size-up completed and the report transmitted",
					"Water supply established and confirmed",
					"Primary search assigned, with the all-clear reported back",
					"RIT / RIC assigned and in position before crews go interior",
					"Ventilation coordinated with the attack line, not ahead of it",
					"Utilities (gas and electric) secured",
					"Exposures checked and protected",
					"20-minute PAR benchmarks running from the time of arrival",
					"Rehab established for crews rotating out",
					"Secondary search assigned once the fire is under control",
					"Fire investigator requested before overhaul destroys the origin area"
				},
				KeyRoles = new[]
				{
					IncidentRoleType.IncidentCommander,
					IncidentRoleType.SafetyOfficer,
					IncidentRoleType.OperationsSectionChief,
					IncidentRoleType.DivisionGroupSupervisor,
					IncidentRoleType.StagingAreaManager,
					IncidentRoleType.RehabOfficer
				},
				SuggestedQuestions = new[]
				{
					"PAR",
					"Do I have a RIT?",
					"What objectives are still open?",
					"Who is working Division A?",
					"What am I missing on a structure fire?"
				}
			},
			new IncidentPlaybook
			{
				Type = IncidentPlaybookType.Wildland,
				DisplayName = "Wildland fire",
				Keywords = new[]
				{
					"wildland", "wild land", "brush fire", "brush", "grass fire", "vegetation fire",
					"wildfire", "forest fire", "timber", "red flag", "field fire", "woods fire"
				},
				Benchmarks = new[]
				{
					"LCES briefed",
					"Anchor point established",
					"Line construction started",
					"Structure triage complete",
					"Containment percentage reported",
					"Fire contained",
					"Fire controlled"
				},
				Checklist = new[]
				{
					"LCES briefed to every division: Lookouts, Communications, Escape routes, Safety zones",
					"Anchor point established before any line construction",
					"Current and forecast wind, humidity and temperature checked",
					"Fire weather watch / red flag warning checked for the burn period",
					"Structure triage assigned for threatened structures",
					"Evacuation warnings and orders coordinated with law enforcement",
					"Air operations coordinated, with an Air Operations Branch Director once aircraft are working",
					"Acreage and containment percentage tracked for the ICS-209",
					"Divisions assigned by geography, each with a named supervisor",
					"Water tender / supply shuttle plan set",
					"Operational period and written IAP set — wildland incidents outlast the first crews"
				},
				KeyRoles = new[]
				{
					IncidentRoleType.IncidentCommander,
					IncidentRoleType.SafetyOfficer,
					IncidentRoleType.OperationsSectionChief,
					IncidentRoleType.DivisionGroupSupervisor,
					IncidentRoleType.PlanningSectionChief,
					IncidentRoleType.LogisticsSectionChief,
					IncidentRoleType.AirOperationsBranchDirector
				},
				SuggestedQuestions = new[]
				{
					"What is the wind doing?",
					"PAR",
					"Span of control",
					"What needs are still open?",
					"What am I missing on a wildland fire?"
				}
			},
			new IncidentPlaybook
			{
				Type = IncidentPlaybookType.VehicleAccident,
				DisplayName = "Vehicle accident",
				Keywords = new[]
				{
					"mva", "mvc", "vehicle accident", "vehicle collision", "traffic collision",
					"car accident", "auto accident", "rollover", "pin in", "entrapment", "extrication",
					"vehicle vs", "car vs", "motorcycle accident", "vehicle fire", "car fire", "tc with"
				},
				Benchmarks = new[]
				{
					"Scene stabilized",
					"Traffic control established",
					"Patient count confirmed",
					"Extrication complete",
					"All patients transported",
					"Roadway released"
				},
				Checklist = new[]
				{
					"Blocking apparatus positioned upstream, wheels turned away from the work area",
					"Patient count confirmed and transmitted",
					"Vehicles stabilized before anyone works in or under them",
					"Hazards checked: fuel, battery, undeployed airbags, hybrid/EV high voltage, cargo",
					"Extrication group assigned with a stated plan and a backup plan",
					"Transport resources requested to match the confirmed patient count",
					"Air medical requested early and a landing zone secured if transport time drives it",
					"Law enforcement notified for investigation and roadway closure",
					"Fluid containment and clean-up arranged before the roadway is released"
				},
				KeyRoles = new[]
				{
					IncidentRoleType.IncidentCommander,
					IncidentRoleType.SafetyOfficer,
					IncidentRoleType.OperationsSectionChief,
					IncidentRoleType.TriageOfficer,
					IncidentRoleType.TransportOfficer
				},
				SuggestedQuestions = new[]
				{
					"How many patients do we have?",
					"What objectives are still open?",
					"What resources do I have on scene?",
					"PAR",
					"What am I missing on a vehicle accident?"
				}
			},
			new IncidentPlaybook
			{
				Type = IncidentPlaybookType.Ems,
				DisplayName = "EMS incident",
				Keywords = new[]
				{
					"ems", "medical", "sick person", "chest pain", "cardiac", "cardiac arrest", "stroke",
					"overdose", "od", "fall", "difficulty breathing", "unconscious", "unresponsive",
					"seizure", "diabetic", "allergic reaction", "lift assist", "bleeding", "trauma"
				},
				Benchmarks = new[]
				{
					"Patient contact made",
					"ALS on scene",
					"Transport decision made",
					"Patient transported"
				},
				Checklist = new[]
				{
					"Scene safety confirmed; staged clear if the scene is not secured",
					"Patient count confirmed",
					"ALS resource on scene or en route when the patient's condition needs it",
					"Receiving facility notified early for time-critical patients (STEMI, stroke, trauma)",
					"Air medical considered when ground transport time is the limiting factor",
					"Extra hands requested for lift assist, long carry-out or difficult access",
					"Law enforcement requested for violence, weapons or a crime scene",
					"Family and bystander management assigned on a working code"
				},
				KeyRoles = new[]
				{
					IncidentRoleType.IncidentCommander,
					IncidentRoleType.MedicalUnitLeader,
					IncidentRoleType.TransportOfficer
				},
				SuggestedQuestions = new[]
				{
					"What resources do I have on scene?",
					"Incident status",
					"What objectives are still open?",
					"PAR",
					"What am I missing on an EMS call?"
				}
			},
			new IncidentPlaybook
			{
				Type = IncidentPlaybookType.MassCasualty,
				DisplayName = "Mass casualty incident",
				Keywords = new[]
				{
					"mci", "mass casualty", "mass cas", "multi casualty", "multiple patients",
					"bus accident", "bus crash", "train derailment", "building collapse with victims",
					"multiple victims"
				},
				Benchmarks = new[]
				{
					"MCI declared",
					"Triage complete",
					"Treatment area established",
					"Transport officer tracking",
					"All immediate patients transported",
					"All patients transported"
				},
				Checklist = new[]
				{
					"MCI declared and the level passed to dispatch",
					"Triage, Treatment and Transport Officers assigned",
					"START / SALT triage complete with counts by category (Immediate, Delayed, Minor, Deceased)",
					"Treatment area and casualty collection point established clear of the hazard",
					"Ambulance staging and a one-way transport corridor separated from the incoming route",
					"Hospital capability / bed poll requested and patients distributed across facilities",
					"Patient tracking in place — every patient's destination recorded",
					"Additional transport, mutual aid and buses requested early rather than late",
					"Medical Branch Director assigned once triage exceeds span of control",
					"Family reunification point and a single public-information release point established"
				},
				KeyRoles = new[]
				{
					IncidentRoleType.IncidentCommander,
					IncidentRoleType.SafetyOfficer,
					IncidentRoleType.MedicalBranchDirector,
					IncidentRoleType.TriageOfficer,
					IncidentRoleType.TreatmentOfficer,
					IncidentRoleType.TransportOfficer,
					IncidentRoleType.StagingAreaManager,
					IncidentRoleType.PublicInformationOfficer
				},
				SuggestedQuestions = new[]
				{
					"Which ICS positions are unfilled?",
					"What needs are still open?",
					"What resources do I have on scene?",
					"PAR",
					"What am I missing on an MCI?"
				}
			},
			new IncidentPlaybook
			{
				Type = IncidentPlaybookType.HazMat,
				DisplayName = "HazMat incident",
				Keywords = new[]
				{
					"hazmat", "haz mat", "hazardous material", "chemical spill", "chemical leak",
					"gas leak", "natural gas", "propane leak", "fuel spill", "unknown odor", "odor of gas",
					"carbon monoxide", "co alarm", "radiological", "biological", "decon", "tanker rollover"
				},
				Benchmarks = new[]
				{
					"Product identified",
					"Zones established",
					"Decon operational",
					"Isolation distance set",
					"Product controlled",
					"Scene turned over"
				},
				Checklist = new[]
				{
					"Approached and staged upwind and uphill, outside the hot zone",
					"Product identified (placard, UN number, SDS) and the ERG isolation distance applied",
					"Hot, warm and cold zones established and physically marked",
					"Decon corridor operational BEFORE any entry team makes entry",
					"Entry team, backup team, entry time and air supply tracked",
					"HazMat Group Supervisor and Decon Officer assigned",
					"Downwind population identified; evacuate or shelter-in-place decision made",
					"Wind direction and forecast checked, and re-checked as the incident runs",
					"Technical reference, shipper and responsible party contacted",
					"Environmental agency and clean-up contractor notified"
				},
				KeyRoles = new[]
				{
					IncidentRoleType.IncidentCommander,
					IncidentRoleType.SafetyOfficer,
					IncidentRoleType.OperationsSectionChief,
					IncidentRoleType.HazMatGroupSupervisor,
					IncidentRoleType.DeconOfficer,
					IncidentRoleType.EntryTeamLeader
				},
				SuggestedQuestions = new[]
				{
					"What is the wind doing?",
					"Which ICS positions are unfilled?",
					"What objectives are still open?",
					"PAR",
					"What am I missing on a HazMat incident?"
				}
			},
			new IncidentPlaybook
			{
				Type = IncidentPlaybookType.NaturalDisaster,
				DisplayName = "Natural disaster",
				Keywords = new[]
				{
					"flood", "flooding", "tornado", "hurricane", "typhoon", "earthquake", "storm damage",
					"severe weather", "ice storm", "blizzard", "mudslide", "landslide", "wind damage",
					"tsunami", "wildfire evacuation", "disaster"
				},
				Benchmarks = new[]
				{
					"Life safety sweep started",
					"Damage assessment started",
					"Shelters opened",
					"Utilities coordinated",
					"Operational period published"
				},
				Checklist = new[]
				{
					"Life-safety sweep of the affected area assigned by division / geography",
					"Damage assessment teams assigned and reporting on a schedule",
					"Shelter and mass-care coordination started with partner agencies",
					"EOC activated, or a liaison established with the jurisdiction's EOC",
					"Utility companies engaged for downed lines, gas and water",
					"Road closures, access routes and staging mapped for incoming resources",
					"Operational periods declared — this incident will outlast the first crews",
					"Logistics plan for fuel, food, rest and relief crews",
					"Documentation Unit tracking costs and resource time for reimbursement",
					"Single public-information release point established"
				},
				KeyRoles = new[]
				{
					IncidentRoleType.IncidentCommander,
					IncidentRoleType.SafetyOfficer,
					IncidentRoleType.OperationsSectionChief,
					IncidentRoleType.PlanningSectionChief,
					IncidentRoleType.LogisticsSectionChief,
					IncidentRoleType.LiaisonOfficer,
					IncidentRoleType.PublicInformationOfficer,
					IncidentRoleType.ShelterMassCareCoordinator,
					IncidentRoleType.DamageAssessmentLead
				},
				SuggestedQuestions = new[]
				{
					"Which ICS positions are unfilled?",
					"Span of control",
					"What needs are still open?",
					"Incident status",
					"What am I missing on a natural disaster?"
				}
			},
			new IncidentPlaybook
			{
				Type = IncidentPlaybookType.SearchAndRescue,
				DisplayName = "Search and rescue",
				Keywords = new[]
				{
					"search and rescue", "sar", "missing person", "missing child", "missing subject",
					"lost hiker", "overdue hiker", "overdue", "walkaway", "despondent", "wandering",
					"lost person", "search"
				},
				Benchmarks = new[]
				{
					"Last known point established",
					"Containment established",
					"Hasty search complete",
					"Segments assigned",
					"Subject located"
				},
				Checklist = new[]
				{
					"Last known point / point last seen established and time-stamped",
					"Subject profile built: age, medical, clothing, experience, intent",
					"Containment set — trailheads, roads and perimeter covered before the search area grows",
					"Hasty teams pushed into the high-probability areas first",
					"Search segments defined, assigned and tracked with coverage / probability of detection",
					"Radio check schedule for every field team, with an overdue trigger",
					"Clue log maintained and every clue investigated and located",
					"Air, K9, drone and technical resources requested early",
					"Cell phone ping / forensics requested through law enforcement",
					"Operational period, night operations and relief teams planned"
				},
				KeyRoles = new[]
				{
					IncidentRoleType.IncidentCommander,
					IncidentRoleType.SafetyOfficer,
					IncidentRoleType.OperationsSectionChief,
					IncidentRoleType.SearchGroupSupervisor,
					IncidentRoleType.PlanningSectionChief,
					IncidentRoleType.LogisticsSectionChief
				},
				SuggestedQuestions = new[]
				{
					"Who is in the field right now?",
					"What objectives are still open?",
					"PAR",
					"What has happened in the last 30 minutes?",
					"What am I missing on a search?"
				}
			},
			new IncidentPlaybook
			{
				Type = IncidentPlaybookType.TechnicalRescue,
				DisplayName = "Technical rescue",
				Keywords = new[]
				{
					"technical rescue", "confined space", "trench", "trench collapse", "high angle",
					"rope rescue", "machinery entrapment", "structural collapse", "collapse rescue",
					"elevator rescue", "industrial accident", "silo", "grain bin"
				},
				Benchmarks = new[]
				{
					"Scene secured",
					"Atmosphere monitored",
					"Rescue versus recovery declared",
					"Patient contact made",
					"Patient extricated",
					"All crews out and accounted for"
				},
				Checklist = new[]
				{
					"Rescue versus recovery decision made and announced to everyone working",
					"Atmospheric monitoring and ventilation done before any confined-space entry",
					"Lock-out / tag-out of machinery and every energy source",
					"Trench: shoring in place and spoil pile set back — nobody enters an unprotected trench",
					"Technical rescue team requested; untrained crews are not committed",
					"Dedicated backup team and a safety officer assigned to the rescue itself",
					"Patient packaging plan set and a transport resource on scene",
					"Structural engineer and utility support requested for a collapse"
				},
				KeyRoles = new[]
				{
					IncidentRoleType.IncidentCommander,
					IncidentRoleType.SafetyOfficer,
					IncidentRoleType.OperationsSectionChief,
					IncidentRoleType.EntryTeamLeader
				},
				SuggestedQuestions = new[]
				{
					"PAR",
					"Do I have a safety officer?",
					"What objectives are still open?",
					"What resources do I have on scene?",
					"What am I missing on a technical rescue?"
				}
			},
			new IncidentPlaybook
			{
				Type = IncidentPlaybookType.WaterRescue,
				DisplayName = "Water rescue",
				Keywords = new[]
				{
					"water rescue", "swift water", "swiftwater", "drowning", "capsized", "boat in distress",
					"ice rescue", "dive rescue", "person in the water", "flood rescue"
				},
				Benchmarks = new[]
				{
					"Downstream containment established",
					"Rescue resources deployed",
					"Subject located",
					"All crews accounted for"
				},
				Checklist = new[]
				{
					"Reach, throw, row, go — the lowest-risk option that works is the right one",
					"Downstream containment and backup established before any in-water attempt",
					"PFDs and throw bags on everyone working at the water's edge",
					"Rescue versus recovery decision made, with time in the water tracked",
					"Boat, dive and helicopter resources requested early",
					"Upstream spotter posted for debris and changing flow"
				},
				KeyRoles = new[]
				{
					IncidentRoleType.IncidentCommander,
					IncidentRoleType.SafetyOfficer,
					IncidentRoleType.OperationsSectionChief
				},
				SuggestedQuestions = new[]
				{
					"PAR",
					"What resources do I have on scene?",
					"What objectives are still open?",
					"Incident status",
					"What am I missing on a water rescue?"
				}
			},
			new IncidentPlaybook
			{
				Type = IncidentPlaybookType.ActiveThreat,
				DisplayName = "Active threat",
				Keywords = new[]
				{
					"active shooter", "active threat", "shooting", "shots fired", "stabbing",
					"hostile event", "bomb threat", "explosion", "civil unrest", "violent incident"
				},
				Benchmarks = new[]
				{
					"Unified command established",
					"Staging established",
					"Casualty collection point established",
					"Patients transported",
					"Scene turned over to law enforcement"
				},
				Checklist = new[]
				{
					"Unified Command established with law enforcement",
					"Staging set well away from the scene and out of line of sight",
					"Warm and cold zones defined by law enforcement — nothing enters the hot zone",
					"Rescue Task Forces formed with force protection if the model is in use",
					"Casualty collection point and an evacuation corridor established",
					"Hemorrhage-control supplies pushed forward to the point of injury",
					"Hospitals notified of a mass-casualty penetrating-trauma event",
					"Secondary device / secondary threat considered before crews are committed",
					"Reunification, public information and behavioral health support started early"
				},
				KeyRoles = new[]
				{
					IncidentRoleType.IncidentCommander,
					IncidentRoleType.UnifiedCommandMember,
					IncidentRoleType.SafetyOfficer,
					IncidentRoleType.MedicalBranchDirector,
					IncidentRoleType.TriageOfficer,
					IncidentRoleType.TransportOfficer,
					IncidentRoleType.LiaisonOfficer,
					IncidentRoleType.PublicInformationOfficer
				},
				SuggestedQuestions = new[]
				{
					"Which ICS positions are unfilled?",
					"PAR",
					"What resources do I have on scene?",
					"What has happened in the last 15 minutes?",
					"What am I missing on an active threat?"
				}
			}
		};

		/// <summary>Every playbook, with the general one first.</summary>
		public static IReadOnlyList<IncidentPlaybook> All => new[] { GeneralPlaybook }.Concat(Playbooks).ToList();

		/// <summary>The always-applicable baseline playbook.</summary>
		public static IncidentPlaybook General => GeneralPlaybook;

		public static IncidentPlaybook Get(IncidentPlaybookType type)
			=> type == IncidentPlaybookType.General
				? GeneralPlaybook
				: Playbooks.FirstOrDefault(p => p.Type == type) ?? GeneralPlaybook;

		/// <summary>
		/// Resolves a playbook from free text the user typed ("structure fire", "mci", "wildland").
		/// Null when nothing matches, so callers can fall back to inference from the call itself.
		/// </summary>
		public static IncidentPlaybook Resolve(string text)
		{
			if (string.IsNullOrWhiteSpace(text))
				return null;

			var needle = text.Trim().ToLowerInvariant();

			foreach (var playbook in Playbooks)
			{
				if (string.Equals(playbook.DisplayName, needle, StringComparison.OrdinalIgnoreCase))
					return playbook;

				if (playbook.Keywords.Any(k => needle.Contains(k)))
					return playbook;
			}

			return null;
		}

		/// <summary>
		/// Infers the incident family from the call's type, name and nature. Scores every playbook by how
		/// many of its keywords appear (longer keywords score higher so "vehicle fire" beats a bare
		/// "fire"), and falls back to the general playbook when nothing scores.
		/// </summary>
		public static IncidentPlaybook Infer(Call call, string incidentName = null)
		{
			if (call == null && string.IsNullOrWhiteSpace(incidentName))
				return GeneralPlaybook;

			var haystack = string.Join(" ", new[]
			{
				incidentName,
				call?.Type,
				call?.Name,
				call?.NatureOfCall == null ? null : Resgrid.Framework.StringHelpers.StripHtmlTagsCharArray(call.NatureOfCall)
			}.Where(s => !string.IsNullOrWhiteSpace(s))).ToLowerInvariant();

			if (haystack.Length == 0)
				return GeneralPlaybook;

			IncidentPlaybook best = null;
			var bestScore = 0;

			foreach (var playbook in Playbooks)
			{
				var score = playbook.Keywords
					.Where(keyword => haystack.Contains(keyword))
					.Select(keyword => keyword.Length)
					.DefaultIfEmpty(0)
					.Max();

				if (score > bestScore)
				{
					bestScore = score;
					best = playbook;
				}
			}

			return best ?? GeneralPlaybook;
		}

		/// <summary>
		/// The checklist to work from for an incident: the type-specific items followed by the general
		/// ones that apply to everything (de-duplicated, type-specific wording winning).
		/// </summary>
		public static IReadOnlyList<string> ChecklistFor(IncidentPlaybook playbook)
		{
			if (playbook == null || playbook.Type == IncidentPlaybookType.General)
				return GeneralPlaybook.Checklist;

			return playbook.Checklist.Concat(GeneralPlaybook.Checklist).ToList();
		}

		/// <summary>Positions worth having filled for an incident: type-specific plus the universal ones.</summary>
		public static IReadOnlyList<IncidentRoleType> KeyRolesFor(IncidentPlaybook playbook)
		{
			if (playbook == null)
				return GeneralPlaybook.KeyRoles;

			return playbook.KeyRoles.Concat(GeneralPlaybook.KeyRoles).Distinct().ToList();
		}
	}
}
