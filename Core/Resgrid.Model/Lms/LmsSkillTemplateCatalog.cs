using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model.Lms
{
	/// <summary>A Resgrid-authored skill sheet starter (structure only).</summary>
	public sealed class LmsSkillSheetTemplate
	{
		public string Key { get; set; }
		public string Title { get; set; }
		public string Code { get; set; }
		public string Sector { get; set; }
		public LmsSkillSheetKinds Kind { get; set; }
		public string Description { get; set; }
		public decimal PassThreshold { get; set; } = 100m;
		public int TimeLimitSeconds { get; set; }
		public int DefaultMinutes { get; set; } = 30;
		public bool AllowCrewEvaluation { get; set; }
		public List<LmsSkillStep> Steps { get; set; } = new List<LmsSkillStep>();
	}

	/// <summary>A Resgrid-authored task book starter (position codes and task identifiers only).</summary>
	public sealed class LmsTaskBookTemplate
	{
		public string Key { get; set; }
		public string PositionCode { get; set; }
		public string Name { get; set; }
		public LmsTaskBookFamilies Family { get; set; }
		public string Description { get; set; }
		public int ValidityMonths { get; set; } = LmsTaskBookDefinition.DefaultValidityMonths;
		public int RecertifyMonths { get; set; }
		public List<LmsTaskBookTask> Tasks { get; set; } = new List<LmsTaskBookTask>();
	}

	/// <summary>
	/// The skill-sheet template library and task book starters (§13a "skill-sheet template library", following the Checklists template
	/// catalog pattern). Resgrid wrote every step label here; nothing is copied from a standard, a national registry's skill sheet or a
	/// published task book. Wildland task books carry NWCG position codes and task numbers only (the department enters each task's text
	/// from its own copy); officer and driver/operator books carry qualification-level names and short Resgrid task names; NREMT skills
	/// appear by skill name only. A guard test keeps prose out (labels are names, not sentences).
	/// </summary>
	public static class LmsSkillTemplateCatalog
	{
		/// <summary>The longest label a catalog step or task may carry.</summary>
		public const int MaxLabelLength = 80;

		public const string Guidance = "Starting points written by Resgrid. Adapt them to your equipment, procedures and the edition of any standard you train to.";

		private static LmsSkillStep H(string key, string label) => new LmsSkillStep { Key = key, Type = (int)LmsSkillStepTypes.Heading, Label = label };

		private static LmsSkillStep P(string key, string label, bool critical = false) =>
			new LmsSkillStep { Key = key, Type = (int)LmsSkillStepTypes.PassFail, Label = label, Critical = critical, Required = true, RequireNoteOnFail = true };

		private static LmsSkillStep D(string key, string label, int maxSeconds, bool critical = true) =>
			new LmsSkillStep { Key = key, Type = (int)LmsSkillStepTypes.Duration, Label = label, Max = maxSeconds, Critical = critical, Required = true };

		private static LmsSkillStep R(string key, string label) =>
			new LmsSkillStep { Key = key, Type = (int)LmsSkillStepTypes.RatingScale, Label = label, RatingMin = 1, RatingMax = 7, PassingRating = 4, Required = true, AllowNotApplicable = true, RequireNoteOnFail = true };

		private static LmsSkillStep T(string key, string label, bool required = false) =>
			new LmsSkillStep { Key = key, Type = (int)LmsSkillStepTypes.Text, Label = label, Required = required };

		private static LmsSkillStep E(string key, string label) => new LmsSkillStep { Key = key, Type = (int)LmsSkillStepTypes.Evidence, Label = label };

		private static LmsSkillStep N(string key, string label) => new LmsSkillStep { Key = key, Type = (int)LmsSkillStepTypes.TimestampedNotes, Label = label };

		private static LmsSkillSheetTemplate Sheet(string key, string title, string code, string sector, string description, params LmsSkillStep[] steps) =>
			new LmsSkillSheetTemplate { Key = key, Title = title, Code = code, Sector = sector, Description = description, Steps = steps.ToList() };

		public static IReadOnlyList<LmsSkillSheetTemplate> Sheets { get; } = new List<LmsSkillSheetTemplate>
		{
			WithTime(Sheet("scba-donning", "SCBA donning", "SKL-SCBA", "Fire", "Don a self-contained breathing apparatus against the clock.",
				H("h1", "Preparation"), P("inspect", "Inspects the unit before use", true), P("harness", "Dons and adjusts the harness"),
				H("h2", "Facepiece and air"), P("seal", "Dons the facepiece and checks the seal", true), P("air", "Connects the regulator and breathes air", true),
				P("ppe", "Hood and helmet in place, no exposed skin", true), D("time", "Time to breathing air (seconds)", 60),
				E("photo", "Photo or video"), T("comments", "Evaluator comments")), 60),
			Crew(Sheet("hoseline-advance", "Hoseline advance and flow", "SKL-HOSE", "Fire", "Advance a charged attack line and flow water.",
				P("size-up", "Communicates and confirms the assignment"), P("stretch", "Stretches the line without kinks"), P("charge", "Calls for water and bleeds the line"),
				P("advance", "Advances as a team with the nozzle controlled", true), P("flow", "Flows with the correct pattern and stance"), P("safety", "Maintains crew integrity", true),
				T("comments", "Evaluator comments"))),
			Crew(Sheet("ground-ladder", "Ground ladder raise", "SKL-LADDER", "Fire", "Carry, raise and secure a ground ladder.",
				P("carry", "Selects and carries the ladder safely"), P("hazards", "Checks overhead hazards before raising", true), P("raise", "Raises the ladder under control"),
				P("angle", "Sets a safe climbing angle"), P("secure", "Heels or ties off before climbing", true), T("comments", "Evaluator comments"))),
			Sheet("forcible-entry", "Forcible entry (inward-swinging door)", "SKL-FE", "Fire", "Force an inward-swinging door with hand tools.",
				P("size-up", "Sizes up the door and lock"), P("tools", "Selects suitable tools"), P("gap", "Gaps and sets the tool"), P("force", "Forces the door under control"),
				P("control", "Controls the door after entry", true), T("comments", "Evaluator comments")),
			Crew(Sheet("primary-search", "Primary search", "SKL-SEARCH", "Fire", "Search a room or structure as a team.",
				P("ppe", "Full protective equipment and air", true), P("orientation", "Keeps orientation (wall or line)", true), P("pattern", "Searches systematically"),
				P("comms", "Reports progress and findings"), P("victim", "Removes the victim safely"), N("log", "Timed notes"), T("comments", "Evaluator comments"))),
			Sheet("pump-operations", "Pump operations", "SKL-PUMP", "Fire", "Engage the pump and supply attack lines.",
				P("position", "Positions and chocks the apparatus", true), P("engage", "Engages the pump correctly", true), P("tank", "Supplies a line from the tank"),
				P("pressure", "Sets and holds the discharge pressure"), P("supply", "Transitions to a hydrant supply"), new LmsSkillStep { Key = "psi", Type = (int)LmsSkillStepTypes.Number, Label = "Discharge pressure held" },
				T("comments", "Evaluator comments")),
			Sheet("evoc-course", "Emergency vehicle driving course", "SKL-EVOC", "Fire / EMS", "Drive the department's cone course and road route.",
				P("pretrip", "Completes the pre-trip inspection", true), P("serpentine", "Serpentine without striking cones"), P("backing", "Backs with a spotter"),
				P("stop", "Stops within the box"), P("road", "Road drive: intersections and lane use", true), new LmsSkillStep { Key = "miles", Type = (int)LmsSkillStepTypes.Number, Label = "Road miles driven" },
				T("comments", "Evaluator comments")),
			Sheet("pa-trauma", "Patient assessment - trauma", "SKL-PA-T", "EMS", "Assess and manage a trauma patient (scenario).",
				P("scene", "Scene safety and precautions", true), P("primary", "Primary survey and life threats", true), P("history", "History and secondary assessment"),
				P("vitals", "Vital signs"), P("interventions", "Interventions and transport decision", true), P("reassess", "Reassessment"), N("log", "Timed notes"), T("comments", "Evaluator comments")),
			Sheet("bleeding-control", "Bleeding control and shock management", "SKL-BLEED", "EMS", "Control a major bleed and treat for shock.",
				P("precautions", "Precautions", true), P("pressure", "Direct pressure"), P("tourniquet", "Tourniquet when indicated", true), P("shock", "Treats for shock"),
				P("reassess", "Reassesses"), T("comments", "Evaluator comments")),
			Sheet("cardiac-arrest-aed", "Cardiac arrest management / AED", "SKL-CPR", "EMS", "Run a cardiac arrest with an AED.",
				P("recognize", "Recognizes arrest and calls for help", true), P("compressions", "Effective compressions", true), P("aed", "Applies and operates the AED", true),
				P("ventilation", "Ventilates appropriately"), P("roles", "Rotates compressors"), D("time-to-shock", "Time to first shock (seconds)", 120, false), T("comments", "Evaluator comments")),
			Sheet("forklift-operator", "Powered industrial truck operator evaluation", "SKL-PIT", "Industrial", "Workplace evaluation of a forklift operator (re-evaluate every 3 years).",
				P("inspection", "Pre-use inspection", true), P("load", "Picks up and carries a load safely", true), P("travel", "Travels with the load low and visibility clear"),
				P("stack", "Stacks and unstacks"), P("pedestrians", "Yields to pedestrians and uses the horn", true), P("park", "Parks: forks down, brake set, power off", true),
				T("comments", "Evaluator comments")),
			Dor(Sheet("fto-dor-fire", "Daily observation report - probationary firefighter", "DOR-FIRE", "Fire", "Field training officer's daily ratings and narrative.",
				R("knowledge", "Knowledge of apparatus and equipment"), R("safety", "Safety practices"), R("skills", "Fireground skills"), R("comms", "Communication"),
				R("teamwork", "Teamwork and attitude"), new LmsSkillStep { Key = "overall", Type = (int)LmsSkillStepTypes.Computed, Label = "Average rating", ComputeOp = (int)LmsSkillComputeOps.Average, ComputeKeys = new List<string> { "knowledge", "safety", "skills", "comms", "teamwork" } },
				T("strengths", "Most acceptable performance", true), T("improve", "Least acceptable performance", true), T("plan", "Training plan for the next shift"))),
			Dor(Sheet("preceptor-dor-ems", "Daily observation report - EMS preceptor", "DOR-EMS", "EMS", "Preceptor's daily ratings and narrative.",
				R("assessment", "Patient assessment"), R("treatment", "Treatment and protocol knowledge"), R("documentation", "Documentation"), R("communication", "Patient and crew communication"),
				R("safety", "Scene and patient safety"), new LmsSkillStep { Key = "overall", Type = (int)LmsSkillStepTypes.Computed, Label = "Average rating", ComputeOp = (int)LmsSkillComputeOps.Average, ComputeKeys = new List<string> { "assessment", "treatment", "documentation", "communication", "safety" } },
				T("strengths", "Most acceptable performance", true), T("improve", "Least acceptable performance", true))),
			Dor(Sheet("cto-dor-dispatch", "Daily observation report - communications trainee", "DOR-COMM", "Dispatch", "Communications training officer's daily ratings and narrative.",
				R("call-taking", "Call taking"), R("radio", "Radio procedures"), R("cad", "CAD use"), R("multitask", "Multitasking under load"), R("stress", "Stress tolerance"),
				new LmsSkillStep { Key = "overall", Type = (int)LmsSkillStepTypes.Computed, Label = "Average rating", ComputeOp = (int)LmsSkillComputeOps.Average, ComputeKeys = new List<string> { "call-taking", "radio", "cad", "multitask", "stress" } },
				T("strengths", "Most acceptable performance", true), T("improve", "Least acceptable performance", true)))
		}.AsReadOnly();

		private static LmsSkillSheetTemplate WithTime(LmsSkillSheetTemplate template, int seconds)
		{
			template.TimeLimitSeconds = 0;
			template.DefaultMinutes = Math.Max(1, (seconds + 59) / 60);
			return template;
		}

		private static LmsSkillSheetTemplate Crew(LmsSkillSheetTemplate template)
		{
			template.AllowCrewEvaluation = true;
			return template;
		}

		private static LmsSkillSheetTemplate Dor(LmsSkillSheetTemplate template)
		{
			template.Kind = LmsSkillSheetKinds.ObservationReport;
			template.PassThreshold = 50m;
			template.DefaultMinutes = 0;
			return template;
		}

		private static List<LmsTaskBookTask> Numbered(string code, int count, int incidentFrom) =>
			Enumerable.Range(1, count).Select(i => new LmsTaskBookTask
			{
				Key = "t" + i,
				Number = i.ToString(System.Globalization.CultureInfo.InvariantCulture),
				Title = code + " task " + i,
				RequiredEvaluations = 1,
				RequiresIncident = incidentFrom > 0 && i >= incidentFrom
			}).ToList();

		private static List<LmsTaskBookTask> Named(params (string Title, int Required, bool Incident)[] tasks) =>
			tasks.Select((t, i) => new LmsTaskBookTask { Key = "t" + (i + 1), Number = (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), Title = t.Title, RequiredEvaluations = t.Required, RequiresIncident = t.Incident }).ToList();

		private const string WildlandNote = "NWCG position code and task numbers only. Enter each task's text from your copy of the position task book and match the task count to its edition.";

		public static IReadOnlyList<LmsTaskBookTemplate> TaskBooks { get; } = new List<LmsTaskBookTemplate>
		{
			new LmsTaskBookTemplate { Key = "nwcg-fft2", PositionCode = "FFT2", Name = "Firefighter Type 2", Family = LmsTaskBookFamilies.Wildland, Description = WildlandNote, Tasks = Numbered("FFT2", 10, 6) },
			new LmsTaskBookTemplate { Key = "nwcg-fft1", PositionCode = "FFT1", Name = "Firefighter Type 1", Family = LmsTaskBookFamilies.Wildland, Description = WildlandNote, Tasks = Numbered("FFT1", 12, 7) },
			new LmsTaskBookTemplate { Key = "nwcg-engb", PositionCode = "ENGB", Name = "Engine Boss (Single Resource)", Family = LmsTaskBookFamilies.Wildland, Description = WildlandNote, Tasks = Numbered("ENGB", 14, 8) },
			new LmsTaskBookTemplate { Key = "nwcg-ict5", PositionCode = "ICT5", Name = "Incident Commander Type 5", Family = LmsTaskBookFamilies.Wildland, Description = WildlandNote, Tasks = Numbered("ICT5", 12, 6) },
			new LmsTaskBookTemplate
			{
				Key = "fire-officer-1", PositionCode = "FO1", Name = "Fire Officer I", Family = LmsTaskBookFamilies.Officer,
				Description = "Qualification level name only. Add the chapter and JPR numbers of the edition you train to.",
				Tasks = Named(("Company-level tactical command", 2, true), ("Crew supervision and assignments", 1, false), ("Incident reporting", 1, false),
					("Pre-incident planning", 1, false), ("Community risk reduction contact", 1, false), ("Personnel evaluation", 1, false))
			},
			new LmsTaskBookTemplate
			{
				Key = "fire-officer-2", PositionCode = "FO2", Name = "Fire Officer II", Family = LmsTaskBookFamilies.Officer,
				Description = "Qualification level name only. Add the chapter and JPR numbers of the edition you train to.",
				Tasks = Named(("Multi-unit command", 2, true), ("Post-incident analysis", 1, false), ("Fire cause determination", 1, true), ("Budget and records", 1, false),
					("Policy development", 1, false))
			},
			new LmsTaskBookTemplate
			{
				Key = "driver-operator-pumper", PositionCode = "DO-PUMP", Name = "Driver/Operator - Pumper", Family = LmsTaskBookFamilies.DriverOperator,
				Description = "Resgrid task names. Link the driving course and pump operations skill sheets to the tasks.",
				Tasks = Named(("Pre-trip inspection", 2, false), ("Driving course", 1, false), ("Road driving with an evaluator", 3, false), ("Pump from tank", 1, false),
					("Hydrant supply", 1, false), ("Drafting", 1, false), ("Emergency response driving", 2, true))
			},
			new LmsTaskBookTemplate
			{
				Key = "driver-operator-aerial", PositionCode = "DO-AERIAL", Name = "Driver/Operator - Aerial", Family = LmsTaskBookFamilies.DriverOperator,
				Description = "Resgrid task names. Link your aerial skill sheets to the tasks.",
				Tasks = Named(("Pre-trip inspection", 2, false), ("Driving course", 1, false), ("Positioning and stabilization", 2, false), ("Aerial operation", 2, false),
					("Elevated master stream", 1, false))
			},
			new LmsTaskBookTemplate
			{
				Key = "osha-forklift-operator", PositionCode = "PIT", Name = "Powered industrial truck operator", Family = LmsTaskBookFamilies.OshaOperator, RecertifyMonths = 36,
				Description = "Operator training and workplace evaluation; certification is due again every 3 years.",
				Tasks = Named(("Formal instruction completed", 1, false), ("Practical training", 1, false), ("Workplace evaluation", 1, false))
			},
			new LmsTaskBookTemplate
			{
				Key = "nccp-emt-skills", PositionCode = "NCCP-EMT", Name = "NCCP skills verification - EMT", Family = LmsTaskBookFamilies.NccpSkills, ValidityMonths = 24,
				Description = "NREMT skill names only. Signed by the training officer or supervisor (EMR/EMT).",
				Tasks = Named(("Patient Assessment/Management - Trauma", 1, false), ("Patient Assessment/Management - Medical", 1, false), ("BVM Ventilation of an Apneic Adult Patient", 1, false),
					("Oxygen Administration by Non-Rebreather Mask", 1, false), ("Cardiac Arrest Management/AED", 1, false), ("Bleeding Control/Shock Management", 1, false),
					("Spinal Immobilization (Supine Patient)", 1, false), ("Spinal Immobilization (Seated Patient)", 1, false), ("Long Bone Immobilization", 1, false), ("Joint Immobilization", 1, false))
			},
			new LmsTaskBookTemplate
			{
				Key = "nccp-paramedic-skills", PositionCode = "NCCP-P", Name = "NCCP skills verification - Paramedic", Family = LmsTaskBookFamilies.NccpSkills, ValidityMonths = 24,
				Description = "Skill names only. Verified by the medical director (AEMT/Paramedic): set the certifying official to that role.",
				Tasks = Named(("Patient assessment", 1, false), ("Airway management", 1, false), ("Cardiac arrest management", 1, false), ("Medication administration", 1, false),
					("Trauma management", 1, false), ("Pediatric assessment", 1, false))
			},
			new LmsTaskBookTemplate
			{
				Key = "fto-probationary-firefighter", PositionCode = "PROBIE", Name = "Probationary firefighter field training", Family = LmsTaskBookFamilies.FieldTraining, ValidityMonths = 12,
				Description = "Daily observation reports from the field training officer. Link the daily observation report sheet to the first task.",
				Tasks = Named(("Daily observation reports", 10, false), ("Live incident performance", 2, true), ("Final field evaluation", 1, false))
			}
		}.AsReadOnly();

		public static LmsSkillSheetTemplate FindSheet(string key) =>
			Sheets.FirstOrDefault(t => string.Equals(t.Key, (key ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase));

		public static LmsTaskBookTemplate FindTaskBook(string key) =>
			TaskBooks.FirstOrDefault(t => string.Equals(t.Key, (key ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase));
	}
}
