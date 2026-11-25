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
	/// topics. Targets only: ISO section numbering and point values are unverified (§25.5) and are not seeded.
	/// </summary>
	public static class TrainingCategoryTemplateCatalog
	{
		public const string IsoFsrs = "iso-fsrs";
		public const string NremtNccp = "nremt-nccp";
		public const string OshaAnnual = "osha-annual";

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
