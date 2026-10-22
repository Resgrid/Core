using System;
using System.Collections.Generic;
using System.Linq;

namespace Resgrid.Model.RunCards
{
	/// <summary>
	/// A code-only starter run card (conditional run cards). Resources are named by meaning (unit classes, call type
	/// classes, built-in priorities) with suggested names, so a template fits a department whatever it called its units.
	/// Applying one builds draft packages and a disabled card the department reviews and tests before enabling.
	/// </summary>
	public class RunCardTemplate
	{
		public string Id { get; set; }

		public string Category { get; set; }

		public string Name { get; set; }

		public string Description { get; set; }

		public string[] Keywords { get; set; } = Array.Empty<string>();

		public string SearchText { get; set; }

		/// <summary>Call type meaning the card triggers on (CallTypeClasses); null = any type.</summary>
		public CallTypeClasses? CallTypeClass { get; set; }

		/// <summary>Built-in priority meaning the card triggers on; null = any priority.</summary>
		public CallPriority? Priority { get; set; }

		/// <summary>Suggested dispatch mode (DispatchRecommendationModes); null keeps the department default.</summary>
		public int? DispatchModeOverride { get; set; }

		public bool RelaxUnfilledRows { get; set; }

		/// <summary>ResponseCodeTemplateCatalog set the rows' codes come from; null when the template uses none.</summary>
		public string ResponseCodeSetId { get; set; }

		public List<RunCardTemplatePackage> Packages { get; set; } = new List<RunCardTemplatePackage>();

		public List<RunCardTemplateLevel> Levels { get; set; } = new List<RunCardTemplateLevel>();

		public bool HasGraph => Levels.Any(l => l.Nodes.Any());

		/// <summary>Every target the template names (rows, substitutes and conditions), for matching and suggestions.</summary>
		public IEnumerable<RunCardTemplateTarget> AllTargets()
		{
			return Packages.SelectMany(p => p.Rows).SelectMany(r => r.Targets)
				.Concat(Levels.SelectMany(l => l.Nodes).SelectMany(n => n.SubjectTargets.Concat(n.ReferenceTargets)));
		}
	}

	public class RunCardTemplateTarget
	{
		public RunCardTargetKinds Kind { get; set; }

		/// <summary>Unit class a unit target means (UnitClass and UnitType kinds).</summary>
		public UnitTypeClasses? UnitClass { get; set; }

		/// <summary>Unit type or role names tried first, case-insensitive.</summary>
		public string[] SuggestedNames { get; set; } = Array.Empty<string>();

		public string Describe()
		{
			switch (Kind)
			{
				case RunCardTargetKinds.AnyUnit:
					return "Any unit";
				case RunCardTargetKinds.AnyPerson:
					return "Any person";
				case RunCardTargetKinds.PersonnelRole:
					return SuggestedNames.FirstOrDefault() ?? "Role";
				default:
					return SuggestedNames.FirstOrDefault() ?? (UnitClass.HasValue ? UnitClass.Value.ToString() : "Unit");
			}
		}
	}

	public class RunCardTemplateRow
	{
		public string Label { get; set; }

		public int RequiredCount { get; set; } = 1;

		public RequirementFillModes FillMode { get; set; } = RequirementFillModes.Count;

		public RunCardStatusFilters StatusFilter { get; set; }

		/// <summary>Code text from the template's code set (e.g. "Code 3").</summary>
		public string ResponseCode { get; set; }

		/// <summary>Key of the template package that substitutes for this row.</summary>
		public string SubstitutePackageKey { get; set; }

		public bool KeepOutstanding { get; set; }

		public List<RunCardTemplateTarget> Targets { get; set; } = new List<RunCardTemplateTarget>();

		public RunCardTemplateRow Code(string code) { ResponseCode = code; return this; }

		public RunCardTemplateRow Outstanding() { KeepOutstanding = true; return this; }

		public RunCardTemplateRow Substitute(string packageKey) { SubstitutePackageKey = packageKey; return this; }

		public RunCardTemplateRow Filter(RunCardStatusFilters filter) { StatusFilter = filter; return this; }
	}

	public class RunCardTemplatePackage
	{
		/// <summary>Key the template's levels, outcomes and substitutes use for this package.</summary>
		public string Key { get; set; }

		public string Name { get; set; }

		public List<RunCardTemplateRow> Rows { get; set; } = new List<RunCardTemplateRow>();
	}

	public class RunCardTemplateLevel
	{
		public int AlarmLevel { get; set; } = 1;

		public string Name { get; set; }

		/// <summary>Package the level always sends (fixed level, or always rows beside a graph).</summary>
		public string PackageKey { get; set; }

		public List<RunCardTemplateNode> Nodes { get; set; } = new List<RunCardTemplateNode>();
	}

	public class RunCardTemplateNode
	{
		public string Key { get; set; }

		public bool IsRoot { get; set; }

		public RunCardDecisionNodeKinds Kind { get; set; }

		public string Label { get; set; }

		public RunCardConditionTypes ConditionType { get; set; }

		public List<RunCardTemplateTarget> SubjectTargets { get; set; } = new List<RunCardTemplateTarget>();

		public RunCardStatusFilters SubjectStatusFilter { get; set; }

		public List<RunCardTemplateTarget> ReferenceTargets { get; set; } = new List<RunCardTemplateTarget>();

		public RunCardStatusFilters ReferenceStatusFilter { get; set; }

		public int? ThresholdSeconds { get; set; }

		public int RequiredCount { get; set; } = 1;

		public string YesKey { get; set; }

		public string NoKey { get; set; }

		public string PackageKey { get; set; }

		public string AdviceText { get; set; }
	}
}
