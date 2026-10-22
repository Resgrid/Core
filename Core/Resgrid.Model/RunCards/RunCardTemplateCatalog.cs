using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model.RunCards
{
	/// <summary>
	/// Searchable, code-only starter run cards (conditional run cards), one or more per department type. Every template is
	/// generic: resources by meaning plus suggested names, numbers a department edits after applying. Never stored.
	/// </summary>
	public static class RunCardTemplateCatalog
	{
		private const int ClosestUnit = (int)DispatchRecommendationModes.ClosestUnit;

		public static IReadOnlyList<RunCardTemplate> All { get; } = BuildAll();

		public static RunCardTemplate GetById(string id)
		{
			if (string.IsNullOrWhiteSpace(id))
				return null;

			return All.FirstOrDefault(template => string.Equals(template.Id, id, StringComparison.OrdinalIgnoreCase));
		}

		public static IReadOnlyList<RunCardTemplate> GetByCategory(string category)
		{
			return All.Where(template => string.Equals(template.Category, category, StringComparison.OrdinalIgnoreCase)).ToList();
		}

		public static IReadOnlyList<RunCardTemplate> Search(string query)
		{
			if (string.IsNullOrWhiteSpace(query))
				return All;

			var terms = query.ToLowerInvariant().Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
			return All.Where(template => terms.All(term => template.SearchText.Contains(term))).ToList();
		}

		public static IReadOnlyList<string> Categories => All.Select(t => t.Category).Distinct().ToList();

		#region Builders

		private static RunCardTemplateTarget Cls(UnitTypeClasses unitClass, params string[] names)
		{
			return new RunCardTemplateTarget { Kind = RunCardTargetKinds.UnitClass, UnitClass = unitClass, SuggestedNames = names ?? Array.Empty<string>() };
		}

		private static RunCardTemplateTarget Named(UnitTypeClasses unitClass, params string[] names)
		{
			// A named unit type (for example "K9"), falling back to its class when no type has the name.
			return new RunCardTemplateTarget { Kind = RunCardTargetKinds.UnitType, UnitClass = unitClass, SuggestedNames = names ?? Array.Empty<string>() };
		}

		private static RunCardTemplateTarget Role(params string[] names)
		{
			return new RunCardTemplateTarget { Kind = RunCardTargetKinds.PersonnelRole, SuggestedNames = names ?? Array.Empty<string>() };
		}

		private static RunCardTemplateTarget AnyUnit() => new RunCardTemplateTarget { Kind = RunCardTargetKinds.AnyUnit };

		private static RunCardTemplateTarget AnyPerson() => new RunCardTemplateTarget { Kind = RunCardTargetKinds.AnyPerson };

		private static RunCardTemplateRow Row(string label, int count, params RunCardTemplateTarget[] targets)
		{
			return new RunCardTemplateRow { Label = label, RequiredCount = count, Targets = targets.ToList() };
		}

		private static RunCardTemplateRow First(string label, params RunCardTemplateTarget[] targets)
		{
			return new RunCardTemplateRow { Label = label, RequiredCount = 1, FillMode = RequirementFillModes.EnsureFirstArriving, Targets = targets.ToList() };
		}

		private static RunCardTemplatePackage Pkg(string key, string name, params RunCardTemplateRow[] rows)
		{
			return new RunCardTemplatePackage { Key = key, Name = name, Rows = rows.ToList() };
		}

		private static RunCardTemplateLevel Level(int alarmLevel, string name, string packageKey, params RunCardTemplateNode[] nodes)
		{
			return new RunCardTemplateLevel { AlarmLevel = alarmLevel, Name = name, PackageKey = packageKey, Nodes = nodes.ToList() };
		}

		private static RunCardTemplateNode Before(string key, string label, RunCardTemplateTarget[] subject, RunCardTemplateTarget[] reference, int marginSeconds,
			string yes, string no, RunCardStatusFilters subjectFilter = RunCardStatusFilters.AnyAvailable, RunCardStatusFilters referenceFilter = RunCardStatusFilters.AnyAvailable,
			bool root = false)
		{
			return new RunCardTemplateNode
			{
				Key = key, IsRoot = root, Kind = RunCardDecisionNodeKinds.Condition, Label = label, ConditionType = RunCardConditionTypes.ArrivesBefore,
				SubjectTargets = subject.ToList(), SubjectStatusFilter = subjectFilter, ReferenceTargets = reference.ToList(), ReferenceStatusFilter = referenceFilter,
				ThresholdSeconds = marginSeconds, YesKey = yes, NoKey = no
			};
		}

		private static RunCardTemplateNode Within(string key, string label, RunCardTemplateTarget[] subject, int seconds, int count, string yes, string no,
			bool root = false)
		{
			return new RunCardTemplateNode
			{
				Key = key, IsRoot = root, Kind = RunCardDecisionNodeKinds.Condition, Label = label, ConditionType = RunCardConditionTypes.AvailableWithin,
				SubjectTargets = subject.ToList(), ThresholdSeconds = seconds, RequiredCount = count, YesKey = yes, NoKey = no
			};
		}

		private static RunCardTemplateNode Outcome(string key, string label, string packageKey, string advice = null, bool root = false)
		{
			return new RunCardTemplateNode { Key = key, IsRoot = root, Kind = RunCardDecisionNodeKinds.Outcome, Label = label, PackageKey = packageKey, AdviceText = advice };
		}

		private static RunCardTemplateTarget[] T(params RunCardTemplateTarget[] targets) => targets;

		private static RunCardTemplate Template(string id, string category, string name, string description, string[] keywords,
			CallTypeClasses? callTypeClass, CallPriority? priority, RunCardTemplatePackage[] packages, RunCardTemplateLevel[] levels,
			string codeSet = null, int? mode = null, bool relax = false)
		{
			var template = new RunCardTemplate
			{
				Id = id,
				Category = category,
				Name = name,
				Description = description,
				Keywords = keywords ?? Array.Empty<string>(),
				CallTypeClass = callTypeClass,
				Priority = priority,
				Packages = packages.ToList(),
				Levels = levels.ToList(),
				ResponseCodeSetId = codeSet,
				DispatchModeOverride = mode,
				RelaxUnfilledRows = relax
			};

			template.SearchText = string.Join(" ", new[] { template.Name, template.Description, template.Category }.Concat(template.Keywords)).ToLowerInvariant();

			return template;
		}

		#endregion

		// Common targets.
		private static RunCardTemplateTarget Engine => Cls(UnitTypeClasses.Engine, "Engine", "Pumper");
		private static RunCardTemplateTarget Aerial => Cls(UnitTypeClasses.Aerial, "Ladder", "Truck", "Aerial");
		private static RunCardTemplateTarget Quint => Named(UnitTypeClasses.Aerial, "Quint");
		private static RunCardTemplateTarget RescueUnit => Cls(UnitTypeClasses.Rescue, "Rescue", "Heavy Rescue");
		private static RunCardTemplateTarget Tanker => Cls(UnitTypeClasses.Tanker, "Tanker", "Tender", "Water Tender");
		private static RunCardTemplateTarget Ambulance => Cls(UnitTypeClasses.Ambulance, "Ambulance", "Medic");
		private static RunCardTemplateTarget Als => Named(UnitTypeClasses.Ambulance, "ALS Ambulance", "ALS", "Medic");
		private static RunCardTemplateTarget Bls => Named(UnitTypeClasses.Ambulance, "BLS Ambulance", "BLS");
		private static RunCardTemplateTarget MedicalResponse => Cls(UnitTypeClasses.MedicalResponse, "Medical Response", "Squad", "Paramedic Unit");
		private static RunCardTemplateTarget Physician => Named(UnitTypeClasses.MedicalResponse, "Physician", "Physician Car", "Doctor");
		private static RunCardTemplateTarget Wildland => Cls(UnitTypeClasses.Wildland, "Brush", "Wildland", "Type 6");
		private static RunCardTemplateTarget Hazmat => Cls(UnitTypeClasses.HazardousMaterials, "Hazmat", "HazMat");
		private static RunCardTemplateTarget Command => Cls(UnitTypeClasses.Command, "Command", "Chief", "Battalion");
		private static RunCardTemplateTarget Support => Cls(UnitTypeClasses.Support, "Support", "Utility");
		private static RunCardTemplateTarget Watercraft => Cls(UnitTypeClasses.Watercraft, "Boat", "Rescue Boat");
		private static RunCardTemplateTarget Aircraft => Cls(UnitTypeClasses.Aircraft, "Helicopter", "Aircraft");
		private static RunCardTemplateTarget Patrol => Cls(UnitTypeClasses.Patrol, "Patrol", "Patrol Car", "Security Patrol");
		private static RunCardTemplateTarget OtherUnit(params string[] names) => Named(UnitTypeClasses.Other, names);
		private static RunCardTemplateTarget Supervisor => Role("Supervisor", "Shift Supervisor", "Sergeant", "Officer");
		private static RunCardTemplateTarget ChiefRole => Role("Chief", "Battalion Chief", "Duty Officer");

		private static List<RunCardTemplate> BuildAll()
		{
			const string Fire = "Fire Departments";
			const string Ems = "EMS";
			const string Law = "Law Enforcement";
			const string Sar = "Search and Rescue";
			const string Em = "Emergency Management";
			const string Disaster = "Disaster Response";
			const string Security = "Security Companies";
			const string Event = "Event Medical / Security";
			const string Industrial = "Industrial Response";
			const string Delivery = "Delivery Companies";
			const string General = "General";

			var list = new List<RunCardTemplate>
			{
				// ── Fire Departments ──
				Template("fire-structure-first-alarm", Fire, "Structure fire, first alarm", "Three engines, a ladder (a quint stands in and the ladder stays outstanding) and a chief; second and third alarms add more.",
					new[] { "structure", "house", "building", "fire", "alarm" }, CallTypeClasses.Fire, CallPriority.High,
					new[]
					{
						Pkg("first", "Structure fire, first alarm", Row("Engines", 3, Engine).Code("Code 3"), Row("Ladder", 1, Aerial).Code("Code 3").Substitute("quint").Outstanding(), Row("Chief", 1, Command)),
						Pkg("quint", "Quint", Row("Quint", 1, Quint)),
						Pkg("second", "Structure fire, second alarm", Row("Engines", 2, Engine), Row("Ladder", 1, Aerial), Row("Rescue", 1, RescueUnit)),
						Pkg("third", "Structure fire, third alarm", Row("Engines", 3, Engine), Row("Support", 1, Support))
					},
					new[] { Level(1, "First alarm", "first"), Level(2, "Second alarm", "second"), Level(3, "Third alarm", "third") }, "lights-and-siren"),

				Template("fire-alarm-single-engine-upgrade", Fire, "Alarm activation, single engine with upgrade", "One engine when it can arrive within 8 minutes; otherwise the full first alarm.",
					new[] { "alarm", "automatic", "fire alarm", "upgrade" }, CallTypeClasses.Alarm, CallPriority.Medium,
					new[]
					{
						Pkg("single", "Single engine", Row("Engine", 1, Engine)),
						Pkg("full", "Alarm, full first alarm", Row("Engines", 2, Engine), Row("Ladder", 1, Aerial), Row("Chief", 1, Command))
					},
					new[] { Level(1, "Alarm", null,
						Within("near", "Engine within 8 minutes?", T(Engine), 480, 1, "one", "all", root: true),
						Outcome("one", "Single engine", "single"),
						Outcome("all", "Full first alarm", "full")) }, mode: ClosestUnit),

				Template("fire-effective-response-force", Fire, "Effective response force", "Two engines within 8 minutes is enough; otherwise add an engine and a rescue.",
					new[] { "erf", "effective response force", "nfpa", "fire" }, CallTypeClasses.Fire, CallPriority.Emergency,
					new[]
					{
						Pkg("base", "Effective response force", Row("Engines", 2, Engine).Code("Code 3"), Row("Ladder", 1, Aerial).Code("Code 3")),
						Pkg("plus", "Effective response force, reinforced", Row("Engines", 3, Engine).Code("Code 3"), Row("Ladder", 1, Aerial).Code("Code 3"), Row("Rescue", 1, RescueUnit))
					},
					new[] { Level(1, "First alarm", null,
						Within("erf", "Two engines within 8 minutes?", T(Engine), 480, 2, "ok", "more", root: true),
						Outcome("ok", "Effective response force", "base"),
						Outcome("more", "Reinforced response", "plus")) }, "lights-and-siren", ClosestUnit),

				Template("fire-rural-water-supply", Fire, "Rural structure fire with water supply", "Two engines and two tankers; mutual aid stands in for missing tankers and they stay outstanding.",
					new[] { "rural", "tanker", "tender", "water supply", "shuttle" }, CallTypeClasses.Fire, CallPriority.High,
					new[]
					{
						Pkg("rural", "Rural structure fire", Row("Engines", 2, Engine), Row("Tankers", 2, Tanker).Substitute("aid").Outstanding()),
						Pkg("aid", "Mutual aid tanker", Row("Mutual aid unit", 1, OtherUnit("Mutual Aid", "Mutual Aid Tanker")))
					},
					new[] { Level(1, "First alarm", "rural") }),

				Template("fire-ladder-or-quint", Fire, "Ladder or quint", "Send the quint when it beats the ladder by 4 minutes or more; otherwise the ladder.",
					new[] { "ladder", "quint", "aerial", "truck" }, CallTypeClasses.Fire, CallPriority.High,
					new[] { Pkg("quint", "Quint", Row("Quint", 1, Quint)), Pkg("ladder", "Ladder", Row("Ladder", 1, Aerial)) },
					new[] { Level(1, "First alarm", null,
						Before("q", "Quint at least 4 minutes before the ladder?", T(Quint), T(Aerial), 240, "quint", "ladder", root: true),
						Outcome("quint", "Quint", "quint"),
						Outcome("ladder", "Ladder", "ladder")) }, mode: ClosestUnit),

				Template("fire-vehicle", Fire, "Vehicle fire", "One engine, lights and siren.",
					new[] { "vehicle", "car", "auto fire" }, CallTypeClasses.Fire, CallPriority.Medium,
					new[] { Pkg("vehicle", "Vehicle fire", Row("Engine", 1, Engine).Code("Code 3")) },
					new[] { Level(1, "Response", "vehicle") }, "lights-and-siren"),

				Template("fire-wildland-initial-attack", Fire, "Wildland initial attack", "Two wildland units within 15 minutes; otherwise add engines and a water tender.",
					new[] { "wildland", "brush", "grass", "forest" }, CallTypeClasses.Fire, CallPriority.High,
					new[]
					{
						Pkg("ia", "Wildland initial attack", Row("Wildland units", 2, Wildland)),
						Pkg("ia-plus", "Wildland initial attack, reinforced", Row("Wildland units", 2, Wildland), Row("Engines", 2, Engine), Row("Water tender", 1, Tanker))
					},
					new[] { Level(1, "Initial attack", null,
						Within("ia", "Two wildland units within 15 minutes?", T(Wildland), 900, 2, "ok", "more", root: true),
						Outcome("ok", "Initial attack", "ia"),
						Outcome("more", "Reinforced initial attack", "ia-plus")) }, mode: ClosestUnit),

				Template("fire-hazardous-materials", Fire, "Hazardous materials", "Engine, hazmat unit (kept outstanding, mutual aid stands in), chief and an ambulance on standby.",
					new[] { "hazmat", "chemical", "spill", "leak" }, CallTypeClasses.HazardousMaterials, CallPriority.High,
					new[]
					{
						Pkg("hazmat", "Hazardous materials", Row("Engine", 1, Engine), Row("Hazmat unit", 1, Hazmat).Substitute("aid").Outstanding(), Row("Chief", 1, Command), Row("Ambulance standby", 1, Ambulance)),
						Pkg("aid", "Mutual aid hazmat", Row("Mutual aid hazmat", 1, OtherUnit("Mutual Aid", "Mutual Aid Hazmat")))
					},
					new[] { Level(1, "Response", "hazmat") }),

				Template("fire-technical-rescue", Fire, "Technical rescue", "Rescue, engine and ambulance; the second level adds technical rescue specialists.",
					new[] { "technical rescue", "rope", "trench", "collapse" }, CallTypeClasses.Rescue, CallPriority.High,
					new[]
					{
						Pkg("tr1", "Technical rescue", Row("Rescue", 1, RescueUnit), Row("Engine", 1, Engine), Row("Ambulance", 1, Ambulance)),
						Pkg("tr2", "Technical rescue specialists", Row("Technical rescue technicians", 4, Role("Technical Rescue", "Rescue Technician")))
					},
					new[] { Level(1, "Response", "tr1"), Level(2, "Specialists", "tr2") }),

				Template("fire-collision-extrication", Fire, "Traffic collision with extrication", "Engine and ambulance; the second level adds a rescue.",
					new[] { "mva", "collision", "extrication", "crash" }, CallTypeClasses.TrafficCollision, CallPriority.High,
					new[] { Pkg("tc1", "Traffic collision", Row("Engine", 1, Engine), Row("Ambulance", 1, Ambulance)), Pkg("tc2", "Extrication", Row("Rescue", 1, RescueUnit)) },
					new[] { Level(1, "Response", "tc1"), Level(2, "Extrication", "tc2") }),

				Template("fire-medical-first-response", Fire, "Medical first response", "Send an engine as first responder only when it beats the ambulance by 2 minutes or more.",
					new[] { "first responder", "medical", "engine", "fr" }, CallTypeClasses.Medical, CallPriority.High,
					new[] { Pkg("amb", "Ambulance", Row("Ambulance", 1, Ambulance)), Pkg("amb-fr", "Ambulance and first responder", Row("Ambulance", 1, Ambulance), Row("First responder engine", 1, Engine)) },
					new[] { Level(1, "Response", null,
						Before("fr", "Engine at least 2 minutes before the ambulance?", T(Engine), T(Ambulance), 120, "both", "amb", root: true),
						Outcome("both", "Ambulance and first responder", "amb-fr"),
						Outcome("amb", "Ambulance only", "amb")) }, mode: ClosestUnit),

				// ── EMS ──
				Template("ems-als-bls-tiered", Ems, "ALS/BLS tiered response", "A BLS ambulance when it beats the ALS unit by 5 minutes, with ALS intercepting; otherwise the ALS unit.",
					new[] { "als", "bls", "tiered", "paramedic", "emt" }, CallTypeClasses.Medical, CallPriority.High,
					new[] { Pkg("als", "ALS response", Row("ALS ambulance", 1, Als).Code("Code 3")), Pkg("bls-als", "BLS with ALS intercept", Row("BLS ambulance", 1, Bls).Code("Code 3"), Row("ALS intercept", 1, Als).Code("Code 3")) },
					new[] { Level(1, "Response", null,
						Before("tier", "BLS at least 5 minutes before ALS?", T(Bls), T(Als), 300, "bls", "als", root: true),
						Outcome("bls", "BLS with ALS intercept", "bls-als"),
						Outcome("als", "ALS", "als")) }, "lights-and-siren", ClosestUnit),

				Template("ems-cardiac-arrest-all-call", Ems, "Cardiac arrest, all call", "The fastest unit or person whatever its type, plus an ALS and a BLS ambulance and a supervisor.",
					new[] { "cardiac arrest", "all call", "echo", "cpr", "code" }, CallTypeClasses.Medical, CallPriority.Emergency,
					new[] { Pkg("arrest", "Cardiac arrest, all call", First("Closest resource", AnyUnit(), AnyPerson()), Row("ALS ambulance", 1, Als).Code("Code 3"), Row("BLS ambulance", 1, Bls).Code("Code 3"), Row("Supervisor", 1, Supervisor)) },
					new[] { Level(1, "Response", "arrest") }, "lights-and-siren", ClosestUnit),

				Template("ems-closest-ambulance", Ems, "Closest ambulance", "The closest ambulance of any type.",
					new[] { "ambulance", "closest", "transport" }, CallTypeClasses.Medical, CallPriority.Medium,
					new[] { Pkg("amb", "Closest ambulance", Row("Ambulance", 1, Ambulance)) },
					new[] { Level(1, "Response", "amb") }, mode: ClosestUnit),

				Template("ems-interfacility-transfer", Ems, "Interfacility transfer", "One BLS ambulance, routine response.",
					new[] { "transfer", "interfacility", "ift", "transport" }, CallTypeClasses.NonEmergencyActivity, CallPriority.Low,
					new[] { Pkg("ift", "Interfacility transfer", Row("BLS ambulance", 1, Bls).Code("Code 1")) },
					new[] { Level(1, "Response", "ift") }, "lights-and-siren"),

				Template("ems-paramedic-intercept", Ems, "Paramedic intercept", "BLS transports and ALS intercepts when the BLS ambulance is 5 minutes closer; otherwise ALS alone.",
					new[] { "intercept", "paramedic", "als", "bls" }, CallTypeClasses.Medical, CallPriority.High,
					new[] { Pkg("als", "ALS response", Row("ALS ambulance", 1, Als)), Pkg("intercept", "BLS with paramedic intercept", Row("BLS ambulance", 1, Bls), Row("Paramedic intercept", 1, MedicalResponse)) },
					new[] { Level(1, "Response", null,
						Before("int", "BLS at least 5 minutes before ALS?", T(Bls), T(Als), 300, "intercept", "als", root: true),
						Outcome("intercept", "BLS with intercept", "intercept"),
						Outcome("als", "ALS", "als")) }, mode: ClosestUnit),

				Template("ems-mass-casualty", Ems, "Mass casualty incident", "Four ambulances, a supervisor and an engine; then four more; then six more with support.",
					new[] { "mci", "mass casualty", "multiple patients" }, CallTypeClasses.Medical, CallPriority.Emergency,
					new[]
					{
						Pkg("mci1", "MCI level 1", Row("Ambulances", 4, Ambulance), Row("EMS supervisor", 1, Supervisor), Row("Engine", 1, Engine)),
						Pkg("mci2", "MCI level 2", Row("Ambulances", 4, Ambulance)),
						Pkg("mci3", "MCI level 3", Row("Ambulances", 6, Ambulance), Row("Support", 1, Support))
					},
					new[] { Level(1, "MCI level 1", "mci1"), Level(2, "MCI level 2", "mci2"), Level(3, "MCI level 3", "mci3") }),

				Template("ems-behavioural-health-crisis", Ems, "Behavioural health crisis", "A crisis team member and an ambulance, with advice to request law enforcement if violence is reported.",
					new[] { "mental health", "crisis", "behavioural", "behavioral", "psych" }, CallTypeClasses.ServiceCall, CallPriority.Medium,
					new[] { Pkg("crisis", "Behavioural health crisis", Row("Crisis team", 1, Role("Crisis Counselor", "Crisis Team", "Mental Health Clinician")), Row("Ambulance", 1, Ambulance)) },
					new[] { Level(1, "Response", null,
						Outcome("crisis", "Crisis response", "crisis", "Request law enforcement standby if violence or weapons are reported.", root: true) ) }),

				Template("ems-coverage-preferring-ambulance", Ems, "Coverage-preferring ambulance", "Prefer an ambulance already out of quarters when it is not more than 3 minutes slower, keeping stations covered.",
					new[] { "coverage", "system status", "posting", "ambulance" }, CallTypeClasses.Medical, CallPriority.Medium,
					new[] { Pkg("away", "Ambulance away from quarters", Row("Ambulance", 1, Ambulance).Filter(RunCardStatusFilters.AwayFromQuarters)), Pkg("closest", "Closest ambulance", Row("Ambulance", 1, Ambulance)) },
					new[] { Level(1, "Response", null,
						Before("cov", "In-quarters ambulance at least 3 minutes before the one already out?", T(Ambulance), T(Ambulance), 180, "closest", "away",
							RunCardStatusFilters.InQuarters, RunCardStatusFilters.AwayFromQuarters, root: true),
						Outcome("away", "Ambulance away from quarters", "away"),
						Outcome("closest", "Closest ambulance", "closest")) }, mode: ClosestUnit, relax: true),

				Template("ems-physician-paramedic-fallback", Ems, "Physician response with paramedic fallback", "A physician unit with a paramedic unit when the paramedic arrives early enough, otherwise with an ambulance; a missing physician unit is replaced and kept outstanding.",
					new[] { "physician", "doctor", "mug", "smur", "notarzt", "paramedic" }, CallTypeClasses.Medical, CallPriority.Emergency,
					new[]
					{
						Pkg("phys-para", "Physician and paramedic unit", Row("Physician unit", 1, Physician).Substitute("para").Outstanding(), Row("Paramedic unit", 1, MedicalResponse)),
						Pkg("phys-amb", "Physician and ambulance", Row("Physician unit", 1, Physician).Substitute("para").Outstanding(), Row("Ambulance", 1, Ambulance)),
						Pkg("para", "Paramedic unit", Row("Paramedic unit", 1, MedicalResponse))
					},
					new[] { Level(1, "Response", null,
						Before("p1", "Paramedic unit at least 5 minutes before the physician?", T(MedicalResponse), T(Physician), 300, "pp", "p2", root: true),
						Before("p2", "Paramedic unit at least 5 minutes before the ambulance?", T(MedicalResponse), T(Ambulance), 300, "pp", "pa"),
						Outcome("pp", "Physician and paramedic unit", "phys-para"),
						Outcome("pa", "Physician and ambulance", "phys-amb")) }, mode: ClosestUnit, relax: true),

				// ── Law Enforcement ──
				Template("law-priority-1-two-units", Law, "Priority 1, two units", "Two patrol units (the second kept outstanding) and a supervisor.",
					new[] { "priority 1", "emergency", "in progress", "patrol" }, CallTypeClasses.LawEnforcement, CallPriority.Emergency,
					new[] { Pkg("p1", "Priority 1", Row("Primary unit", 1, Patrol).Code("P1"), Row("Cover unit", 1, Patrol).Code("P1").Outstanding(), Row("Supervisor", 1, Supervisor)) },
					new[] { Level(1, "Response", "p1") }, "law-priorities", ClosestUnit),

				Template("law-routine-one-unit", Law, "Routine, one unit", "One patrol unit, routine.",
					new[] { "routine", "report", "patrol" }, CallTypeClasses.LawEnforcement, CallPriority.Low,
					new[] { Pkg("p3", "Routine", Row("Patrol unit", 1, Patrol).Code("P3")) },
					new[] { Level(1, "Response", "p3") }, "law-priorities", ClosestUnit),

				Template("law-officer-needs-assistance", Law, "Officer needs assistance", "The fastest unit of any kind plus four patrol units.",
					new[] { "officer down", "assistance", "help", "backup" }, CallTypeClasses.LawEnforcement, CallPriority.Emergency,
					new[] { Pkg("ona", "Officer needs assistance", First("Closest unit", AnyUnit()), Row("Patrol units", 4, Patrol).Code("P1")) },
					new[] { Level(1, "Response", "ona") }, "law-priorities", ClosestUnit),

				Template("law-injury-collision", Law, "Injury collision", "Patrol, ambulance and engine for multi-discipline departments.",
					new[] { "collision", "injury", "crash", "mva" }, CallTypeClasses.TrafficCollision, CallPriority.High,
					new[] { Pkg("ic", "Injury collision", Row("Patrol", 1, Patrol), Row("Ambulance", 1, Ambulance), Row("Engine", 1, Engine)) },
					new[] { Level(1, "Response", "ic") }),

				Template("law-k9-request", Law, "K9 request", "A K9 unit; a patrol unit stands in and the K9 stays outstanding.",
					new[] { "k9", "canine", "dog", "track" }, CallTypeClasses.LawEnforcement, CallPriority.High,
					new[] { Pkg("k9", "K9 request", Row("K9 unit", 1, Named(UnitTypeClasses.Patrol, "K9", "K-9", "Canine")).Substitute("patrol").Outstanding()), Pkg("patrol", "Patrol unit", Row("Patrol unit", 1, Patrol)) },
					new[] { Level(1, "Response", "k9") }),

				// ── Search and Rescue ──
				Template("sar-lost-person-hasty", Sar, "Lost person, hasty search", "Two ground teams within an hour; otherwise the teams plus callout advice.",
					new[] { "lost", "missing", "hasty", "search" }, CallTypeClasses.Rescue, CallPriority.High,
					new[] { Pkg("hasty", "Hasty search", Row("Ground teams", 2, Named(UnitTypeClasses.Rescue, "Ground Team", "Search Team"))) },
					new[] { Level(1, "Hasty search", null,
						Within("gt", "Two ground teams within 60 minutes?", T(Named(UnitTypeClasses.Rescue, "Ground Team", "Search Team")), 3600, 2, "go", "callout", root: true),
						Outcome("go", "Hasty search", "hasty"),
						Outcome("callout", "Hasty search with callout", "hasty", "Page the full team and request neighbouring SAR teams.")) }, mode: ClosestUnit),

				Template("sar-water-rescue", Sar, "Water rescue", "A boat (a shore team stands in, the boat stays outstanding), two rescue swimmers and an ambulance.",
					new[] { "water", "swiftwater", "drowning", "boat" }, CallTypeClasses.Rescue, CallPriority.Emergency,
					new[]
					{
						Pkg("water", "Water rescue", Row("Boat", 1, Watercraft).Substitute("shore").Outstanding(), Row("Rescue swimmers", 2, Role("Rescue Swimmer", "Swiftwater Technician")), Row("Ambulance", 1, Ambulance)),
						Pkg("shore", "Shore team", Row("Shore team", 1, Named(UnitTypeClasses.Rescue, "Shore Team", "Rescue")))
					},
					new[] { Level(1, "Response", "water") }),

				Template("sar-air-support", Sar, "Air support", "An aircraft, kept outstanding with a ground team standing in.",
					new[] { "air", "helicopter", "aviation" }, CallTypeClasses.Rescue, CallPriority.High,
					new[] { Pkg("air", "Air support", Row("Aircraft", 1, Aircraft).Substitute("ground").Outstanding()), Pkg("ground", "Ground team", Row("Ground team", 1, Named(UnitTypeClasses.Rescue, "Ground Team", "Search Team"))) },
					new[] { Level(1, "Response", "air") }),

				Template("sar-team-callout", Sar, "Team callout", "Six SAR members, closest first.",
					new[] { "callout", "page", "members", "team" }, CallTypeClasses.Rescue, CallPriority.Medium,
					new[] { Pkg("callout", "SAR team callout", Row("SAR members", 6, Role("SAR Member", "Searcher", "Field Member"))) },
					new[] { Level(1, "Callout", "callout") }, mode: ClosestUnit),

				// ── Emergency Management ──
				Template("em-eoc-activation", Em, "EOC activation levels", "Duty officer; then section chiefs; then the full EOC.",
					new[] { "eoc", "activation", "emergency operations" }, CallTypeClasses.DisasterWeather, CallPriority.High,
					new[]
					{
						Pkg("eoc1", "EOC monitoring", Row("Duty officer", 1, Role("Duty Officer", "Emergency Manager"))),
						Pkg("eoc2", "EOC partial activation", Row("Section chiefs", 4, Role("Section Chief", "Operations Section Chief", "Planning Section Chief"))),
						Pkg("eoc3", "EOC full activation", Row("EOC staff", 8, Role("EOC Staff", "EOC Member")))
					},
					new[] { Level(1, "Monitoring", "eoc1"), Level(2, "Partial activation", "eoc2"), Level(3, "Full activation", "eoc3") }),

				Template("em-severe-weather-staging", Em, "Severe weather staging", "Two support units with staging advice.",
					new[] { "weather", "storm", "staging", "tornado", "hurricane" }, CallTypeClasses.DisasterWeather, CallPriority.Medium,
					new[] { Pkg("stage", "Severe weather staging", Row("Support units", 2, Support)) },
					new[] { Level(1, "Staging", null, Outcome("stage", "Staging", "stage", "Stage units at the designated staging area and confirm road status before moving.", root: true)) }),

				Template("em-shelter-operations", Em, "Shelter operations", "Shelter team members and a support unit.",
					new[] { "shelter", "evacuation", "mass care" }, CallTypeClasses.NonEmergencyActivity, CallPriority.Medium,
					new[] { Pkg("shelter", "Shelter operations", Row("Shelter team", 4, Role("Shelter Team", "Shelter Manager", "Mass Care")), Row("Support unit", 1, Support)) },
					new[] { Level(1, "Response", "shelter") }),

				// ── Disaster Response ──
				Template("disaster-damage-assessment", Disaster, "Damage assessment", "Two assessment teams.",
					new[] { "damage", "assessment", "survey" }, CallTypeClasses.DisasterWeather, CallPriority.Medium,
					new[] { Pkg("assess", "Damage assessment", Row("Assessment teams", 2, Named(UnitTypeClasses.Support, "Assessment Team", "Support"))) },
					new[] { Level(1, "Response", "assess") }),

				Template("disaster-flood-high-water", Disaster, "Flood and high water", "A boat when it beats a high-water vehicle by 10 minutes; otherwise the high-water vehicle.",
					new[] { "flood", "high water", "boat" }, CallTypeClasses.DisasterWeather, CallPriority.High,
					new[] { Pkg("boat", "Boat response", Row("Boat", 1, Watercraft)), Pkg("hwv", "High-water vehicle", Row("High-water vehicle", 1, Named(UnitTypeClasses.Support, "High Water Vehicle", "High-Water Vehicle"))) },
					new[] { Level(1, "Response", null,
						Before("fl", "Boat at least 10 minutes before the high-water vehicle?", T(Watercraft), T(Named(UnitTypeClasses.Support, "High Water Vehicle", "High-Water Vehicle")), 600, "boat", "hwv", root: true),
						Outcome("boat", "Boat", "boat"),
						Outcome("hwv", "High-water vehicle", "hwv")) }, mode: ClosestUnit),

				Template("disaster-mutual-aid-request", Disaster, "Mutual aid request", "Advice only: request mutual aid through the agreed channel.",
					new[] { "mutual aid", "request", "assistance" }, CallTypeClasses.MutualAid, CallPriority.High,
					new RunCardTemplatePackage[0],
					new[] { Level(1, "Request", null, Outcome("aid", "Request mutual aid", null, "Request mutual aid through the agreed channel and record the resources promised.", root: true)) }),

				// ── Security Companies ──
				Template("security-alarm-response", Security, "Alarm response", "The closest patrol when it can arrive within 10 minutes; otherwise the patrol with advice to notify the client.",
					new[] { "alarm", "intrusion", "patrol" }, CallTypeClasses.Alarm, CallPriority.Medium,
					new[] { Pkg("patrol", "Alarm response", Row("Patrol", 1, Patrol)) },
					new[] { Level(1, "Response", null,
						Within("near", "Patrol within 10 minutes?", T(Patrol), 600, 1, "go", "late", root: true),
						Outcome("go", "Patrol", "patrol"),
						Outcome("late", "Patrol, notify client", "patrol", "Notify the client that the response will exceed 10 minutes.")) }, mode: ClosestUnit),

				Template("security-incident-escalation", Security, "Incident escalation", "Two patrols and a supervisor, plus whoever is on site first.",
					new[] { "escalation", "incident", "supervisor" }, CallTypeClasses.Other, CallPriority.High,
					new[] { Pkg("esc", "Incident escalation", First("On-site guard", AnyPerson()), Row("Patrols", 2, Patrol), Row("Supervisor", 1, Supervisor)) },
					new[] { Level(1, "Response", "esc") }, mode: ClosestUnit),

				Template("security-medical-assist", Security, "Medical assist", "The first first-aider to arrive, with advice to call EMS.",
					new[] { "medical", "first aid", "injury" }, CallTypeClasses.Medical, CallPriority.High,
					new[] { Pkg("fa", "Medical assist", First("First aider", Role("First Aid", "First Aider"))) },
					new[] { Level(1, "Response", null, Outcome("fa", "First aid", "fa", "Call EMS and send someone to meet them at the entrance.", root: true)) }, mode: ClosestUnit),

				// ── Event Medical / Security ──
				Template("event-nearest-medical-team", Event, "Nearest medical team", "The medical cart when it arrives at least a minute before a foot team; otherwise the foot team.",
					new[] { "event", "medical", "cart", "foot team" }, CallTypeClasses.Medical, CallPriority.High,
					new[] { Pkg("cart", "Medical cart", Row("Medical cart", 1, Named(UnitTypeClasses.MedicalResponse, "Medical Cart", "Gator"))), Pkg("foot", "Foot medical team", Row("Foot team", 1, Named(UnitTypeClasses.MedicalResponse, "Foot Team", "Bike Team"))) },
					new[] { Level(1, "Response", null,
						Before("c", "Cart at least 1 minute before a foot team?", T(Named(UnitTypeClasses.MedicalResponse, "Medical Cart", "Gator")), T(Named(UnitTypeClasses.MedicalResponse, "Foot Team", "Bike Team")), 60, "cart", "foot", root: true),
						Outcome("cart", "Medical cart", "cart"),
						Outcome("foot", "Foot team", "foot")) }, mode: ClosestUnit),

				Template("event-crowd-disturbance", Event, "Crowd disturbance", "Two security teams, a supervisor and medical standby.",
					new[] { "crowd", "disturbance", "fight" }, CallTypeClasses.Other, CallPriority.High,
					new[] { Pkg("crowd", "Crowd disturbance", Row("Security teams", 2, Patrol), Row("Supervisor", 1, Supervisor), Row("Medical standby", 1, MedicalResponse)) },
					new[] { Level(1, "Response", "crowd") }, mode: ClosestUnit),

				Template("event-lost-child", Event, "Lost child", "A security team plus whoever is closest, with search advice.",
					new[] { "lost child", "missing", "child" }, CallTypeClasses.Other, CallPriority.High,
					new[] { Pkg("lost", "Lost child", Row("Security team", 1, Patrol), First("Closest person", AnyPerson())) },
					new[] { Level(1, "Response", null, Outcome("lost", "Lost child", "lost", "Close the exits, broadcast the description and keep the reporting parent at a fixed point.", root: true)) }, mode: ClosestUnit),

				// ── Industrial Response ──
				Template("industrial-plant-fire-alarm", Industrial, "Plant fire alarm", "Four ERT members and the plant engine, with advice to notify the municipal fire department.",
					new[] { "plant", "ert", "fire alarm", "industrial" }, CallTypeClasses.Alarm, CallPriority.High,
					new[] { Pkg("pfa", "Plant fire alarm", Row("ERT members", 4, Role("ERT", "Emergency Response Team", "ERT Member")), Row("Plant engine", 1, Engine)) },
					new[] { Level(1, "Response", null, Outcome("pfa", "Plant fire alarm", "pfa", "Notify the municipal fire department.", root: true)) }),

				Template("industrial-chemical-release", Industrial, "Chemical release", "Four hazmat team members and a medic, with evacuation advice.",
					new[] { "chemical", "release", "hazmat", "leak" }, CallTypeClasses.HazardousMaterials, CallPriority.Emergency,
					new[] { Pkg("chem", "Chemical release", Row("Hazmat team", 4, Role("Hazmat Technician", "Hazmat Team")), Row("Medic", 1, Role("Medic", "ERT Medic", "EMT"))) },
					new[] { Level(1, "Response", null, Outcome("chem", "Chemical release", "chem", "Start the evacuation or shelter-in-place plan for the affected area.", root: true)) }),

				Template("industrial-injury-first-aid", Industrial, "Injury, first aid", "The first first-aider to arrive and an ERT medic.",
					new[] { "injury", "first aid", "medic" }, CallTypeClasses.Medical, CallPriority.High,
					new[] { Pkg("inj", "Injury response", First("First aider", Role("First Aider", "First Aid")), Row("ERT medic", 1, Role("ERT Medic", "Medic", "EMT"))) },
					new[] { Level(1, "Response", "inj") }, mode: ClosestUnit),

				Template("industrial-confined-space", Industrial, "Confined space rescue", "Three rescue team members and a standby attendant.",
					new[] { "confined space", "rescue", "entry" }, CallTypeClasses.Rescue, CallPriority.Emergency,
					new[] { Pkg("cs", "Confined space rescue", Row("Rescue team", 3, Role("Confined Space Rescue", "Rescue Team")), Row("Standby attendant", 1, Role("Attendant", "Standby Attendant"))) },
					new[] { Level(1, "Response", "cs") }),

				// ── Delivery Companies ──
				Template("delivery-closest-driver", Delivery, "Closest driver", "The closest delivery vehicle of any type.",
					new[] { "delivery", "driver", "closest" }, CallTypeClasses.ServiceCall, CallPriority.Medium,
					new[] { Pkg("drv", "Closest driver", Row("Delivery vehicle", 1, AnyUnit())) },
					new[] { Level(1, "Dispatch", "drv") }, mode: ClosestUnit),

				Template("delivery-priority", Delivery, "Priority delivery", "The van when it arrives at least 10 minutes before a truck; otherwise the truck.",
					new[] { "priority", "van", "truck", "delivery" }, CallTypeClasses.ServiceCall, CallPriority.High,
					new[] { Pkg("van", "Van", Row("Van", 1, OtherUnit("Van", "Delivery Van"))), Pkg("truck", "Truck", Row("Truck", 1, OtherUnit("Truck", "Box Truck"))) },
					new[] { Level(1, "Dispatch", null,
						Before("v", "Van at least 10 minutes before a truck?", T(OtherUnit("Van", "Delivery Van")), T(OtherUnit("Truck", "Box Truck")), 600, "van", "truck", root: true),
						Outcome("van", "Van", "van"),
						Outcome("truck", "Truck", "truck")) }, mode: ClosestUnit),

				Template("delivery-roadside-assistance", Delivery, "Roadside assistance", "A service vehicle, kept outstanding with any vehicle standing in.",
					new[] { "roadside", "breakdown", "service" }, CallTypeClasses.ServiceCall, CallPriority.Medium,
					new[] { Pkg("rs", "Roadside assistance", Row("Service vehicle", 1, OtherUnit("Service Vehicle", "Service Truck")).Substitute("any").Outstanding()), Pkg("any", "Any vehicle", Row("Any vehicle", 1, AnyUnit())) },
					new[] { Level(1, "Dispatch", "rs") }, mode: ClosestUnit),

				// ── General ──
				Template("general-closest-unit", General, "Closest available unit", "The closest available unit of any type.",
					new[] { "closest", "any", "general" }, null, null,
					new[] { Pkg("closest", "Closest unit", Row("Closest unit", 1, AnyUnit())) },
					new[] { Level(1, "Response", "closest") }, mode: ClosestUnit),

				Template("general-all-call", General, "All call", "The fastest unit or person whatever its type, plus the three closest people.",
					new[] { "all call", "everyone", "fastest" }, null, CallPriority.Emergency,
					new[] { Pkg("all", "All call", First("Fastest resource", AnyUnit(), AnyPerson()), Row("Closest people", 3, AnyPerson())) },
					new[] { Level(1, "Response", "all") }, mode: ClosestUnit),

				Template("general-two-tier-escalation", General, "Two-tier escalation", "One unit; then two more; then three more.",
					new[] { "escalation", "tiers", "alarm levels" }, null, CallPriority.Medium,
					new[] { Pkg("t1", "Tier 1", Row("Unit", 1, AnyUnit())), Pkg("t2", "Tier 2", Row("Units", 2, AnyUnit())), Pkg("t3", "Tier 3", Row("Units", 3, AnyUnit())) },
					new[] { Level(1, "Tier 1", "t1"), Level(2, "Tier 2", "t2"), Level(3, "Tier 3", "t3") }, mode: ClosestUnit),

				Template("general-supervisor-notify", General, "Supervisor response", "The closest unit plus a supervisor.",
					new[] { "supervisor", "general" }, null, CallPriority.High,
					new[] { Pkg("sup", "Unit and supervisor", Row("Closest unit", 1, AnyUnit()), Row("Supervisor", 1, Supervisor)) },
					new[] { Level(1, "Response", "sup") }, mode: ClosestUnit),

				Template("general-chief-notification", General, "Command officer response", "Closest unit plus a command officer for high-priority calls.",
					new[] { "chief", "command", "officer" }, null, CallPriority.Emergency,
					new[] { Pkg("cmd", "Unit and command officer", Row("Closest unit", 1, AnyUnit()), Row("Command officer", 1, ChiefRole)) },
					new[] { Level(1, "Response", "cmd") }, mode: ClosestUnit)
			};

			return list;
		}
	}

	/// <summary>Starter response code sets a template can add to a department's code catalog.</summary>
	public static class ResponseCodeTemplateCatalog
	{
		public class CodeSet
		{
			public string Id { get; set; }
			public string Name { get; set; }
			public List<DispatchResponseCode> Codes { get; set; } = new List<DispatchResponseCode>();
		}

		private static DispatchResponseCode C(string code, string name, string color) => new DispatchResponseCode { Code = code, Name = name, Color = color };

		public static IReadOnlyList<CodeSet> All { get; } = new List<CodeSet>
		{
			new CodeSet { Id = "lights-and-siren", Name = "Lights and siren", Codes = { C("Code 3", "Lights and siren", "#d9534f"), C("Code 2", "Urgent, no lights", "#f0ad4e"), C("Code 1", "Routine", "#5cb85c") } },
			new CodeSet { Id = "hot-cold", Name = "Hot / cold", Codes = { C("Hot", "Hot response", "#d9534f"), C("Cold", "Cold response", "#5bc0de") } },
			new CodeSet { Id = "response-grades", Name = "Response grades", Codes = { C("Immediate", "Immediate", "#d9534f"), C("Prompt", "Prompt", "#f0ad4e"), C("Scheduled", "Scheduled", "#5cb85c") } },
			new CodeSet { Id = "lettered-levels", Name = "Lettered response levels", Codes = { C("A", "Level A", "#5cb85c"), C("B", "Level B", "#5bc0de"), C("C", "Level C", "#f0ad4e"), C("D", "Level D", "#d9534f"), C("E", "Level E", "#8b0000") } },
			new CodeSet { Id = "law-priorities", Name = "Law enforcement priorities", Codes = { C("P1", "Emergency", "#d9534f"), C("P2", "Urgent", "#f0ad4e"), C("P3", "Routine", "#5cb85c"), C("P4", "Report only", "#777777") } },
			new CodeSet { Id = "industrial-security", Name = "Industrial and security", Codes = { C("Immediate", "Immediate", "#d9534f"), C("Standard", "Standard", "#f0ad4e"), C("Scheduled", "Scheduled", "#5cb85c") } },
			new CodeSet { Id = "care-categories", Name = "Care categories", Codes = { C("Cat 1", "Immediately life-threatening", "#8b0000"), C("Cat 2", "Emergency", "#d9534f"), C("Cat 3", "Urgent", "#f0ad4e"), C("Cat 4", "Less urgent", "#5bc0de"), C("Cat 5", "Non-urgent transport", "#5cb85c") } }
		};

		public static CodeSet GetById(string id)
		{
			return string.IsNullOrWhiteSpace(id) ? null : All.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
		}
	}
}
