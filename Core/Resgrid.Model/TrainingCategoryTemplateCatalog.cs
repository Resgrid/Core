using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model
{
	/// <summary>One category of a vertical template.</summary>
	public sealed class TrainingCategoryTemplateItem
	{
		public string Code { get; set; }
		public string Name { get; set; }
		public string Description { get; set; }
		public decimal? TargetHours { get; set; }
		public TrainingTargetPeriods TargetPeriod { get; set; }
		public string TargetAppliesTo { get; set; }
		public int? RetentionYears { get; set; }
	}

	/// <summary>A vertical's starter set of training categories, applied to a department on request (never automatically).</summary>
	public sealed class TrainingCategoryTemplate
	{
		public string Key { get; set; }
		public string Name { get; set; }
		public string Description { get; set; }
		public TrainingCategoryKinds Kind { get; set; }
		/// <summary>Where the targets come from; Resgrid is not an accreditor and the targets are not point values (§10.6, §14).</summary>
		public string Source { get; set; }
		public IReadOnlyList<TrainingCategoryTemplateItem> Items { get; set; } = new List<TrainingCategoryTemplateItem>();
		/// <summary>G8 CA1: where the template applies ("CA", "CA-ON", "US", "EU"...), or null for a generic template (TemplateJurisdictions).</summary>
		public string Jurisdiction { get; set; }
	}

	/// <summary>NREMT NCCP hours per component for one certification level (two-year cycle, §10.6).</summary>
	public sealed class NccpLevelTarget
	{
		public string Level { get; set; }
		public decimal National { get; set; }
		public decimal Local { get; set; }
		public decimal Individual { get; set; }
		public decimal Total => National + Local + Individual;
	}

	/// <summary>
	/// Resgrid-maintained category templates per vertical (T1 item 1, §10.6): the ISO FSRS training categories with their
	/// maximum-credit targets, the NREMT NCCP components (targets by level in <see cref="NccpTargets"/>) and the OSHA annual
	/// topics. Targets only: ISO section numbering and point values are unverified (§25.5) and are not seeded. Round 9C adds
	/// sector templates (workplace compliance, industrial and hazardous materials safety, search and rescue, law enforcement
	/// in-service, security officer licensing, CPD) and region templates (Canada, EU) as structure only: their hours are left for
	/// the organization to set from its own regulator or licensing body, and no regulation's text is quoted.
	/// </summary>
	public static class TrainingCategoryTemplateCatalog
	{
		public const string IsoFsrs = "iso-fsrs";
		public const string NremtNccp = "nremt-nccp";
		public const string OshaAnnual = "osha-annual";

		// Round 9C: sector and region templates (structure only; targets are set by the organization and the body it reports to).
		public const string WorkplaceCompliance = "workplace-compliance";
		public const string IndustrialSafety = "industrial-safety";
		public const string SarTeam = "sar-team";
		public const string LawEnforcementInService = "law-enforcement-in-service";
		public const string SecurityLicensing = "security-licensing";
		public const string CpdHours = "cpd-hours";
		public const string CanadaWorkplace = "canada-workplace";
		public const string EuWorkplace = "eu-workplace";

		/// <summary>G8 CA1: region templates (US rating, registry and regulation templates, Canada, EU). The sector templates stay generic.</summary>
		public static readonly IReadOnlyDictionary<string, string> Jurisdictions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			[IsoFsrs] = "US", [NremtNccp] = "US", [OshaAnnual] = "US", [CanadaWorkplace] = "CA", [EuWorkplace] = "EU"
		};

		static TrainingCategoryTemplateCatalog()
		{
			foreach (var template in All)
				if (template.Jurisdiction == null && Jurisdictions.TryGetValue(template.Key, out var code)) template.Jurisdiction = code;
		}

		/// <summary>G8 CA1: the templates offered to a department in <paramref name="jurisdiction"/> (generic ones always).</summary>
		public static IReadOnlyList<TrainingCategoryTemplate> ForJurisdiction(IEnumerable<TrainingCategoryTemplate> templates, string jurisdiction)
			=> TemplateJurisdictions.Filter(templates, t => t.Jurisdiction, jurisdiction);

		public const string NccpNationalCode = "NCCP-NAT";
		public const string NccpLocalCode = "NCCP-LOC";
		public const string NccpIndividualCode = "NCCP-IND";

		public static readonly IReadOnlyList<TrainingCategoryTemplate> All = new List<TrainingCategoryTemplate>
		{
			new TrainingCategoryTemplate
			{
				Key = IsoFsrs, Name = "ISO FSRS training", Kind = TrainingCategoryKinds.IsoFsrs,
				Description = "ISO Fire Suppression Rating Schedule training categories with the hours for maximum credit.",
				Source = "ISO Mitigation, PPC training (values for maximum credit)",
				Items = new List<TrainingCategoryTemplateItem>
				{
					new TrainingCategoryTemplateItem { Code = "ISO-FAC", Name = "Use of training facilities", TargetHours = 18m, TargetPeriod = TrainingTargetPeriods.PerYear, TargetAppliesTo = "per firefighter" },
					new TrainingCategoryTemplateItem { Code = "ISO-CO", Name = "Company training", Description = "16 hours per member per month.", TargetHours = 192m, TargetPeriod = TrainingTargetPeriods.PerYear, TargetAppliesTo = "per member" },
					new TrainingCategoryTemplateItem { Code = "ISO-OFF", Name = "Officer training", Description = "Certified officers plus continuing education.", TargetHours = 12m, TargetPeriod = TrainingTargetPeriods.PerYear, TargetAppliesTo = "per officer" },
					new TrainingCategoryTemplateItem { Code = "ISO-DRV-NEW", Name = "New driver/operator training", TargetHours = 60m, TargetPeriod = TrainingTargetPeriods.Once, TargetAppliesTo = "per new driver/operator" },
					new TrainingCategoryTemplateItem { Code = "ISO-DRV", Name = "Existing driver/operator training", TargetHours = 12m, TargetPeriod = TrainingTargetPeriods.PerYear, TargetAppliesTo = "per driver/operator" },
					new TrainingCategoryTemplateItem { Code = "ISO-HAZ", Name = "Hazardous materials training", TargetHours = 6m, TargetPeriod = TrainingTargetPeriods.PerYear, TargetAppliesTo = "per member" },
					new TrainingCategoryTemplateItem { Code = "ISO-REC", Name = "Recruit training", TargetHours = 240m, TargetPeriod = TrainingTargetPeriods.FirstYear, TargetAppliesTo = "per recruit, first year" },
					// T3 (LMS): the pre-fire planning item building-familiarization credit from Records lands in (counted in inspections, not hours).
					new TrainingCategoryTemplateItem { Code = "ISO-PFP", Name = "Pre-fire planning inspections", Description = "Occupancy inspections and pre-plan reviews (credited from Records with the LMS add-on).", TargetPeriod = TrainingTargetPeriods.None, TargetAppliesTo = "per member" }
				}
			},
			new TrainingCategoryTemplate
			{
				Key = NremtNccp, Name = "NREMT NCCP recertification", Kind = TrainingCategoryKinds.Nccp,
				Description = "The three components of the National Continued Competency Program, counted over the two-year cycle.",
				Source = "NREMT recertification (National 50%, Local 25%, Individual 25%)",
				Items = new List<TrainingCategoryTemplateItem>
				{
					new TrainingCategoryTemplateItem { Code = NccpNationalCode, Name = "NCCP national component", TargetPeriod = TrainingTargetPeriods.PerCycle, TargetAppliesTo = "EMR 8 h, EMT 20 h, AEMT 25 h, Paramedic 30 h per 2-year cycle" },
					new TrainingCategoryTemplateItem { Code = NccpLocalCode, Name = "NCCP local component", TargetPeriod = TrainingTargetPeriods.PerCycle, TargetAppliesTo = "EMR 4 h, EMT 10 h, AEMT 12.5 h, Paramedic 15 h per 2-year cycle" },
					new TrainingCategoryTemplateItem { Code = NccpIndividualCode, Name = "NCCP individual component", TargetPeriod = TrainingTargetPeriods.PerCycle, TargetAppliesTo = "EMR 4 h, EMT 10 h, AEMT 12.5 h, Paramedic 15 h per 2-year cycle" }
				}
			},
			new TrainingCategoryTemplate
			{
				Key = OshaAnnual, Name = "OSHA annual topics", Kind = TrainingCategoryKinds.Osha,
				Description = "Annual OSHA training topics. Bloodborne pathogen training records are kept for three years.",
				Source = "29 CFR 1910.1030 (bloodborne pathogens), 29 CFR 1910.120 (HAZWOPER)",
				Items = new List<TrainingCategoryTemplateItem>
				{
					new TrainingCategoryTemplateItem { Code = "OSHA-BBP", Name = "Bloodborne pathogens (annual)", TargetPeriod = TrainingTargetPeriods.PerYear, TargetAppliesTo = "per exposed member", RetentionYears = 3 },
					new TrainingCategoryTemplateItem { Code = "OSHA-HAZWOPER", Name = "HAZWOPER refresher", TargetHours = 8m, TargetPeriod = TrainingTargetPeriods.PerYear, TargetAppliesTo = "per HAZWOPER-trained member" }
				}
			},
			new TrainingCategoryTemplate
			{
				Key = WorkplaceCompliance, Name = "Workplace compliance training", Kind = TrainingCategoryKinds.Custom,
				Description = "Onboarding, policy acknowledgments and the recurring compliance topics most employers track. Set each target to your own policy.",
				Source = "Resgrid structure; targets set by the organization",
				Items = new List<TrainingCategoryTemplateItem>
				{
					new TrainingCategoryTemplateItem { Code = "WC-ONB", Name = "New employee onboarding", Description = "Policies, the handbook, site rules and safety basics in the first weeks.", TargetPeriod = TrainingTargetPeriods.Once, TargetAppliesTo = "per new employee" },
					new TrainingCategoryTemplateItem { Code = "WC-POL", Name = "Policy acknowledgments", Description = "Handbook, code of conduct and other policies read and acknowledged.", TargetPeriod = TrainingTargetPeriods.PerYear, TargetAppliesTo = "per employee" },
					new TrainingCategoryTemplateItem { Code = "WC-CONDUCT", Name = "Conduct and anti-harassment", Description = "Respect at work, harassment prevention and how to raise a concern.", TargetPeriod = TrainingTargetPeriods.PerYear, TargetAppliesTo = "per employee" },
					new TrainingCategoryTemplateItem { Code = "WC-SAFETY", Name = "Workplace safety", Description = "Hazards, protective equipment, evacuation and reporting injuries and near misses.", TargetPeriod = TrainingTargetPeriods.PerYear, TargetAppliesTo = "per employee" },
					new TrainingCategoryTemplateItem { Code = "WC-DATA", Name = "Data protection and privacy", Description = "Handling personal data, information security and reporting a data breach.", TargetPeriod = TrainingTargetPeriods.PerYear, TargetAppliesTo = "per employee who handles personal data" }
				}
			},
			new TrainingCategoryTemplate
			{
				Key = IndustrialSafety, Name = "Industrial and hazardous materials safety", Kind = TrainingCategoryKinds.Custom,
				Description = "Recurring safety training for industrial sites, hazardous materials and industrial emergency response teams. Structure only: set the hours your regulator and site rules require.",
				Source = "Resgrid structure; targets set by the organization and its regulator",
				Items = new List<TrainingCategoryTemplateItem>
				{
					new TrainingCategoryTemplateItem { Code = "IS-HAZMAT", Name = "Hazardous materials refresher", Description = "The annual refresher for hazardous materials and hazardous waste work.", TargetPeriod = TrainingTargetPeriods.PerYear, TargetAppliesTo = "per worker who handles or responds to hazardous materials" },
					new TrainingCategoryTemplateItem { Code = "IS-CONFINED", Name = "Confined space", Description = "Entry, attendant and rescue training and the practical rescue exercise.", TargetPeriod = TrainingTargetPeriods.PerYear, TargetAppliesTo = "per entrant, attendant and rescuer" },
					new TrainingCategoryTemplateItem { Code = "IS-ENERGY", Name = "Hazardous energy control (lockout/tagout)", Description = "Energy isolation procedures and periodic inspections.", TargetPeriod = TrainingTargetPeriods.PerYear, TargetAppliesTo = "per authorized and affected employee" },
					new TrainingCategoryTemplateItem { Code = "IS-RESP", Name = "Respiratory protection and fit testing", Description = "Respirator use, care and the fit test.", TargetPeriod = TrainingTargetPeriods.PerYear, TargetAppliesTo = "per respirator wearer" },
					new TrainingCategoryTemplateItem { Code = "IS-HAZCOM", Name = "Hazard communication", Description = "Labels, safety data sheets and the chemicals on site.", TargetPeriod = TrainingTargetPeriods.PerYear, TargetAppliesTo = "per employee" },
					new TrainingCategoryTemplateItem { Code = "IS-ERT", Name = "Emergency response team drills", Description = "Site emergency plan drills and exercises.", TargetPeriod = TrainingTargetPeriods.PerYear, TargetAppliesTo = "per team member" }
				}
			},
			new TrainingCategoryTemplate
			{
				Key = SarTeam, Name = "Search and rescue team training", Kind = TrainingCategoryKinds.Custom,
				Description = "The training areas search and rescue teams usually track. Set targets to your team's standard and the agency that holds search authority.",
				Source = "Resgrid structure; targets set by the team",
				Items = new List<TrainingCategoryTemplateItem>
				{
					new TrainingCategoryTemplateItem { Code = "SAR-NAV", Name = "Navigation and mapping", Description = "Map, compass and GPS skills.", TargetPeriod = TrainingTargetPeriods.PerYear, TargetAppliesTo = "per field member" },
					new TrainingCategoryTemplateItem { Code = "SAR-SEARCH", Name = "Search tactics", Description = "Clue awareness, search techniques and tasking.", TargetPeriod = TrainingTargetPeriods.PerYear, TargetAppliesTo = "per field member" },
					new TrainingCategoryTemplateItem { Code = "SAR-MED", Name = "Wilderness and remote first aid", Description = "First aid where help is far away.", TargetPeriod = TrainingTargetPeriods.PerYear, TargetAppliesTo = "per field member" },
					new TrainingCategoryTemplateItem { Code = "SAR-TECH", Name = "Technical rescue", Description = "Rope, water, snow or other technical rescue skills the team offers.", TargetPeriod = TrainingTargetPeriods.PerYear, TargetAppliesTo = "per technical team member" },
					new TrainingCategoryTemplateItem { Code = "SAR-EX", Name = "Exercises and callout drills", Description = "Team exercises, mock searches and callout tests.", TargetPeriod = TrainingTargetPeriods.PerYear, TargetAppliesTo = "per member" }
				}
			},
			new TrainingCategoryTemplate
			{
				Key = LawEnforcementInService, Name = "Law enforcement in-service training", Kind = TrainingCategoryKinds.Custom,
				Description = "The in-service hours and topics police services and other law enforcement agencies report each year. Set the hours your licensing body or training standard requires.",
				Source = "Resgrid structure; hours set by the agency and its licensing body",
				Items = new List<TrainingCategoryTemplateItem>
				{
					new TrainingCategoryTemplateItem { Code = "LE-INSVC", Name = "In-service training hours", Description = "All in-service training counted toward the annual requirement.", TargetPeriod = TrainingTargetPeriods.PerYear, TargetAppliesTo = "per sworn member" },
					new TrainingCategoryTemplateItem { Code = "LE-UOF", Name = "Use of force and de-escalation", Description = "Policy review, de-escalation and scenario training.", TargetPeriod = TrainingTargetPeriods.PerYear, TargetAppliesTo = "per sworn member" },
					new TrainingCategoryTemplateItem { Code = "LE-WEAPONS", Name = "Weapons qualification", Description = "Qualification for each weapon a member is authorized to carry.", TargetPeriod = TrainingTargetPeriods.PerYear, TargetAppliesTo = "per member authorized to carry" },
					new TrainingCategoryTemplateItem { Code = "LE-LEGAL", Name = "Legal updates", Description = "New legislation, court decisions and policy changes.", TargetPeriod = TrainingTargetPeriods.PerYear, TargetAppliesTo = "per sworn member" },
					new TrainingCategoryTemplateItem { Code = "LE-FIRSTAID", Name = "First aid", Description = "First aid and life-saving skills.", TargetPeriod = TrainingTargetPeriods.PerCycle, TargetAppliesTo = "per sworn member" }
				}
			},
			new TrainingCategoryTemplate
			{
				Key = SecurityLicensing, Name = "Security officer licensing hours", Kind = TrainingCategoryKinds.Custom,
				Description = "The training security officers need for their licence and its renewal, plus site training. Set the hours your licensing body requires for the initial licence and each renewal.",
				Source = "Resgrid structure; hours set by the licensing body",
				Items = new List<TrainingCategoryTemplateItem>
				{
					new TrainingCategoryTemplateItem { Code = "SL-INITIAL", Name = "Licensing training (initial)", Description = "The training required before the licence is issued.", TargetPeriod = TrainingTargetPeriods.Once, TargetAppliesTo = "per new officer" },
					new TrainingCategoryTemplateItem { Code = "SL-RENEWAL", Name = "Licence renewal training", Description = "Refresher or continuing training counted toward the licence renewal.", TargetPeriod = TrainingTargetPeriods.PerCycle, TargetAppliesTo = "per licensed officer" },
					new TrainingCategoryTemplateItem { Code = "SL-SITE", Name = "Site and post orders", Description = "Site induction and post orders read and acknowledged.", TargetPeriod = TrainingTargetPeriods.None, TargetAppliesTo = "per officer and site" },
					new TrainingCategoryTemplateItem { Code = "SL-FIRSTAID", Name = "First aid", Description = "First aid and life-saving skills.", TargetPeriod = TrainingTargetPeriods.PerCycle, TargetAppliesTo = "per officer" },
					new TrainingCategoryTemplateItem { Code = "SL-FORCE", Name = "Use of force and restraint", Description = "Where the law and the officer's licence allow it.", TargetPeriod = TrainingTargetPeriods.PerYear, TargetAppliesTo = "per officer authorized to use it" }
				}
			},
			new TrainingCategoryTemplate
			{
				Key = CpdHours, Name = "Continuing professional development (CPD)", Kind = TrainingCategoryKinds.Custom,
				Description = "A neutral structure for CE, CPD or continuing training hours that a professional body or registration requires. Set the hours and the cycle to your own body's rules.",
				Source = "Resgrid structure; hours set by the professional body",
				Items = new List<TrainingCategoryTemplateItem>
				{
					new TrainingCategoryTemplateItem { Code = "CPD-STRUCT", Name = "Structured learning", Description = "Courses, classes, workshops and other formal learning.", TargetPeriod = TrainingTargetPeriods.PerCycle, TargetAppliesTo = "per registered member" },
					new TrainingCategoryTemplateItem { Code = "CPD-SELF", Name = "Self-directed learning", Description = "Reading, research and other learning the member records.", TargetPeriod = TrainingTargetPeriods.PerCycle, TargetAppliesTo = "per registered member" },
					new TrainingCategoryTemplateItem { Code = "CPD-PRACT", Name = "Practice and skills", Description = "Skills practice, drills and supervised practice.", TargetPeriod = TrainingTargetPeriods.PerCycle, TargetAppliesTo = "per registered member" }
				}
			},
			new TrainingCategoryTemplate
			{
				Key = CanadaWorkplace, Name = "Canada workplace training", Kind = TrainingCategoryKinds.Custom,
				Description = "Workplace training employers in Canada commonly track: hazard communication (WHMIS-style), first aid, health and safety committee training and workplace violence and harassment. Structure only: the requirements differ by province and territory, so set each target to the rules where you operate.",
				Source = "Resgrid structure; targets set by the employer under provincial or territorial rules",
				Items = new List<TrainingCategoryTemplateItem>
				{
					new TrainingCategoryTemplateItem { Code = "CA-HAZCOM", Name = "Hazard communication (WHMIS-style)", Description = "Labels, safety data sheets and the products on site; reviewed at least once a year.", TargetPeriod = TrainingTargetPeriods.PerYear, TargetAppliesTo = "per worker who works with or near hazardous products" },
					new TrainingCategoryTemplateItem { Code = "CA-FIRSTAID", Name = "Workplace first aid", Description = "First aid certification at the level the workplace needs.", TargetPeriod = TrainingTargetPeriods.PerCycle, TargetAppliesTo = "per designated first aider" },
					new TrainingCategoryTemplateItem { Code = "CA-JHSC", Name = "Health and safety committee or representative", Description = "Training for the joint health and safety committee or the representative.", TargetPeriod = TrainingTargetPeriods.Once, TargetAppliesTo = "per committee member or representative" },
					new TrainingCategoryTemplateItem { Code = "CA-VIOLENCE", Name = "Workplace violence and harassment", Description = "The employer's policy and program, and how to report.", TargetPeriod = TrainingTargetPeriods.PerYear, TargetAppliesTo = "per worker" },
					new TrainingCategoryTemplateItem { Code = "CA-TDG", Name = "Transportation of dangerous goods", Description = "Training for handling and transporting dangerous goods.", TargetPeriod = TrainingTargetPeriods.PerCycle, TargetAppliesTo = "per worker who handles, offers or transports dangerous goods" }
				}
			},
			new TrainingCategoryTemplate
			{
				Key = EuWorkplace, Name = "EU workplace training", Kind = TrainingCategoryKinds.Custom,
				Description = "Workplace training employers in the European Union commonly track: first aid, fire safety and evacuation, explosive atmospheres (ATEX-style), manual handling and display screen work. Structure only: the requirements differ by member state, so set each target to the national rules where you operate.",
				Source = "Resgrid structure; targets set by the employer under national rules",
				Items = new List<TrainingCategoryTemplateItem>
				{
					new TrainingCategoryTemplateItem { Code = "EU-FIRSTAID", Name = "First aid at work", Description = "First aid training and its refresher cycle.", TargetPeriod = TrainingTargetPeriods.PerCycle, TargetAppliesTo = "per designated first aider" },
					new TrainingCategoryTemplateItem { Code = "EU-FIRE", Name = "Fire safety and evacuation", Description = "Fire prevention, alarms, extinguishers and evacuation drills.", TargetPeriod = TrainingTargetPeriods.PerYear, TargetAppliesTo = "per employee and fire warden" },
					new TrainingCategoryTemplateItem { Code = "EU-ATEX", Name = "Explosive atmospheres (ATEX-style)", Description = "Working safely in areas where an explosive atmosphere can occur.", TargetPeriod = TrainingTargetPeriods.PerYear, TargetAppliesTo = "per worker in a classified zone" },
					new TrainingCategoryTemplateItem { Code = "EU-MANUAL", Name = "Manual handling", Description = "Safe lifting and handling.", TargetPeriod = TrainingTargetPeriods.PerCycle, TargetAppliesTo = "per employee who lifts or carries loads" },
					new TrainingCategoryTemplateItem { Code = "EU-DSE", Name = "Display screen work", Description = "Workstation set-up and breaks.", TargetPeriod = TrainingTargetPeriods.Once, TargetAppliesTo = "per employee who works at a screen" }
				}
			}
		};

		/// <summary>NCCP hours per component by level (two-year cycle, §10.6). Keys match certification codes loosely (see <see cref="NccpLevelFor"/>).</summary>
		public static readonly IReadOnlyList<NccpLevelTarget> NccpTargets = new List<NccpLevelTarget>
		{
			new NccpLevelTarget { Level = "Paramedic", National = 30m, Local = 15m, Individual = 15m },
			new NccpLevelTarget { Level = "AEMT", National = 25m, Local = 12.5m, Individual = 12.5m },
			new NccpLevelTarget { Level = "EMT", National = 20m, Local = 10m, Individual = 10m },
			new NccpLevelTarget { Level = "EMR", National = 8m, Local = 4m, Individual = 4m }
		};

		public static TrainingCategoryTemplate Find(string key) =>
			All.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase));

		/// <summary>The text fields of a template (round 9C): names, descriptions, the source line and who a target applies to. Codes stay.</summary>
		public static readonly ISet<string> TextProperties = SeedContentLocalization.Properties("Name", "Description", "Source", "TargetAppliesTo");

		/// <summary>A template in a language (a translated copy; the template itself in English or when the language ships no variant).</summary>
		public static TrainingCategoryTemplate Localized(TrainingCategoryTemplate template, string culture) =>
			SeedContentLocalization.Localize(template, SeedContentLocalization.CategoryTemplates, culture, TextProperties, template?.Key);

		/// <summary>Every template in a language, in catalog order.</summary>
		public static IReadOnlyList<TrainingCategoryTemplate> AllFor(string culture) => All.Select(t => Localized(t, culture)).ToList();

		/// <summary>One template in a language; null when the key is unknown.</summary>
		public static TrainingCategoryTemplate Find(string key, string culture) => Localized(Find(key), culture);

		/// <summary>Every English text of a template (what the translations cover).</summary>
		public static IReadOnlyList<string> TextOf(TrainingCategoryTemplate template) => SeedContentLocalization.CollectObject(template, TextProperties);

		/// <summary>
		/// The NCCP level a certification type stands for, from its code or name ("NREMT-P", "Paramedic", "AEMT", "EMT-B",
		/// "EMR"); null when none matches. Paramedic and AEMT are tested before EMT, which their names contain.
		/// </summary>
		public static NccpLevelTarget NccpLevelFor(string codeOrName)
		{
			if (string.IsNullOrWhiteSpace(codeOrName))
				return null;

			var text = codeOrName.ToUpperInvariant();
			if (text.Contains("PARAMEDIC") || text.Contains("NRP") || text.EndsWith("-P") || text.Contains("EMT-P"))
				return NccpTargets[0];
			if (text.Contains("AEMT") || text.Contains("ADVANCED EMT") || text.Contains("EMT-A"))
				return NccpTargets[1];
			if (text.Contains("EMR") || text.Contains("FIRST RESPONDER"))
				return NccpTargets[3];
			if (text.Contains("EMT"))
				return NccpTargets[2];
			return null;
		}
	}
}
