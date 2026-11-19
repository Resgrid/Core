using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Mapping
{
	/// <summary>An id and a name, for the rule form's choices and the rules table.</summary>
	public class DestinationRuleOption
	{
		public string Id { get; set; }

		public string Name { get; set; }
	}

	/// <summary>One rule as the rules table shows it: names, not ids; "needs attention" when ids no longer exist.</summary>
	public class DestinationRuleRow
	{
		public string DestinationRuleId { get; set; }

		public string Name { get; set; }

		/// <summary>The call type name, null for any; "(deleted)" text is decided by the view when <see cref="CallTypeMissing"/>.</summary>
		public string CallTypeName { get; set; }

		public bool CallTypeMissing { get; set; }

		public string PriorityName { get; set; }

		public bool PriorityMissing { get; set; }

		public string UnitTypeName { get; set; }

		public bool UnitTypeMissing { get; set; }

		public List<string> DestinationTypeNames { get; set; } = new List<string>();

		public List<string> RequiredCapabilityNames { get; set; } = new List<string>();

		public List<string> PreferredCapabilityNames { get; set; } = new List<string>();

		/// <summary>How many of the rule's types or capabilities were deleted (they are ignored).</summary>
		public int MissingReferenceCount { get; set; }

		/// <summary>The rule named destination types and none is left, so it never applies.</summary>
		public bool AllTypesRemoved { get; set; }
	}

	/// <summary>Mapping → Destination Rules.</summary>
	public class DestinationRulesIndexView
	{
		public DestinationSuggestionModes Mode { get; set; }

		public int PreferredWithinMinutes { get; set; } = DestinationSuggestionConfig.DefaultPreferredWithinMinutes;

		public List<DestinationRuleRow> Rules { get; set; } = new List<DestinationRuleRow>();

		public List<DestinationRuleOption> CallTypes { get; set; } = new List<DestinationRuleOption>();

		public List<DestinationRuleOption> Priorities { get; set; } = new List<DestinationRuleOption>();

		public List<DestinationRuleOption> UnitTypes { get; set; } = new List<DestinationRuleOption>();

		/// <summary>False when the department has no destination POI type yet (rules have nothing to choose from).</summary>
		public bool HasDestinationTypes { get; set; }

		public string Message { get; set; }

		public string ErrorMessage { get; set; }
	}

	/// <summary>Add or edit one destination rule.</summary>
	public class DestinationRuleEditView
	{
		public string DestinationRuleId { get; set; }

		public string Name { get; set; }

		public int? CallTypeId { get; set; }

		public int? CallPriority { get; set; }

		public int? UnitTypeId { get; set; }

		public List<int> PoiTypeIds { get; set; } = new List<int>();

		public List<string> RequiredCapabilityIds { get; set; } = new List<string>();

		public List<string> PreferredCapabilityIds { get; set; } = new List<string>();

		public List<DestinationRuleOption> CallTypes { get; set; } = new List<DestinationRuleOption>();

		public List<DestinationRuleOption> Priorities { get; set; } = new List<DestinationRuleOption>();

		public List<DestinationRuleOption> UnitTypes { get; set; } = new List<DestinationRuleOption>();

		public List<DestinationRuleOption> DestinationTypes { get; set; } = new List<DestinationRuleOption>();

		public List<DestinationRuleOption> Capabilities { get; set; } = new List<DestinationRuleOption>();
	}
}
