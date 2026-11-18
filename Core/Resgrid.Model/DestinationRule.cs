using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>
	/// What a destination must offer for a kind of call or a kind of unit (M0317): "Burns calls need a Burn Center", "a
	/// Helicopter needs a Helipad", "a Water Tender goes to a Water Source that allows Drafting". Each condition is optional
	/// (null = any); a rule with none applies to every call (the department default). Every rule that matches a call (and the
	/// unit travelling) applies together, see <see cref="DestinationRuleResolver"/>. Used to suggest the call's destination
	/// and to rank destinations for a unit's status. No foreign keys: a deleted type, priority or capability simply stops
	/// matching (the resolver drops ids that no longer exist) and the department purge (UnlinkedDepartmentCleanup) removes the rows.
	/// </summary>
	[Table("DestinationRules")]
	public class DestinationRule : IEntity
	{
		public const int MaxNameLength = 100;

		[Key]
		[Required]
		public string DestinationRuleId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		/// <summary>Shown to dispatchers as the reason for a requirement ("Burns", "Helicopter landing").</summary>
		[Required]
		[MaxLength(MaxNameLength)]
		public string Name { get; set; }

		/// <summary>Only calls of this <see cref="CallType.CallTypeId"/>; null for any.</summary>
		public int? CallTypeId { get; set; }

		/// <summary>
		/// Only calls of this priority: the value <see cref="Call.Priority"/> holds (a <see cref="DepartmentCallPriority.DepartmentCallPriorityId"/>,
		/// or 0-3 for the built-in priorities), as run cards match it; null for any.
		/// </summary>
		public int? CallPriority { get; set; }

		/// <summary>Only when a unit of this <see cref="UnitType.UnitTypeId"/> is travelling; null for any (and for the call's own destination).</summary>
		public int? UnitTypeId { get; set; }

		/// <summary>Comma-separated destination <see cref="PoiType.PoiTypeId"/>s the destination must be one of; blank for any destination type.</summary>
		public string PoiTypeIds { get; set; }

		/// <summary>Comma-separated <see cref="PoiCapability.PoiCapabilityId"/>s the destination must offer (all of them).</summary>
		public string RequiredCapabilityIds { get; set; }

		/// <summary>
		/// Comma-separated <see cref="PoiCapability.PoiCapabilityId"/>s that make a destination better when it is about as
		/// close as the closest (within the department's <see cref="DestinationSuggestionConfig.PreferredWithinMinutes"/>).
		/// </summary>
		public string PreferredCapabilityIds { get; set; }

		public string CreatedByUserId { get; set; }

		public DateTime CreatedOn { get; set; }

		public string UpdatedByUserId { get; set; }

		public DateTime? UpdatedOn { get; set; }

		/// <summary>The parsed <see cref="PoiTypeIds"/>, without blanks, duplicates or non-numbers.</summary>
		public List<int> GetPoiTypeIds()
		{
			var ids = new List<int>();
			foreach (var part in PoiAvailabilityRules.ParseIds(PoiTypeIds))
			{
				if (int.TryParse(part, out var id) && id > 0 && !ids.Contains(id))
					ids.Add(id);
			}

			return ids;
		}

		/// <summary>The parsed <see cref="RequiredCapabilityIds"/>.</summary>
		public List<string> GetRequiredCapabilityIds()
		{
			return PoiAvailabilityRules.ParseIds(RequiredCapabilityIds);
		}

		/// <summary>The parsed <see cref="PreferredCapabilityIds"/>.</summary>
		public List<string> GetPreferredCapabilityIds()
		{
			return PoiAvailabilityRules.ParseIds(PreferredCapabilityIds);
		}

		/// <summary>How many conditions the rule sets (0 = applies to every call).</summary>
		[NotMapped]
		public int ConditionCount => (CallTypeId.HasValue ? 1 : 0) + (CallPriority.HasValue ? 1 : 0) + (UnitTypeId.HasValue ? 1 : 0);

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return DestinationRuleId; }
			set { DestinationRuleId = value?.ToString(); }
		}

		[NotMapped]
		public string TableName => "DestinationRules";

		[NotMapped]
		public string IdName => "DestinationRuleId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "ConditionCount" };
	}
}
