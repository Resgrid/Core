using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>
	/// A command-level need for an incident (resources, logistics, medical support, ...) tracked to
	/// fulfillment. Lanes can optionally be tied to a need via <see cref="CommandStructureNode.LinkedNeedId"/>.
	/// </summary>
	public class IncidentNeed : IEntity, IChangeTracked
	{
		public string IncidentNeedId { get; set; }

		public string IncidentCommandId { get; set; }

		public int DepartmentId { get; set; }

		public int CallId { get; set; }

		public string Name { get; set; }

		/// <summary>Optional free-text detail (what exactly is needed, where, by when).</summary>
		public string Description { get; set; }

		/// <summary>Maps to <see cref="IncidentNeedCategory"/>.</summary>
		public int Category { get; set; }

		/// <summary>Maps to <see cref="IncidentNeedStatus"/>.</summary>
		public int Status { get; set; }

		/// <summary>How many of the thing are needed (0 = unquantified).</summary>
		public int QuantityRequested { get; set; }

		/// <summary>How many have been fulfilled so far.</summary>
		public int QuantityFulfilled { get; set; }

		/// <summary>Relative priority for triage/ordering (0 = unset; higher = more urgent).</summary>
		public int Priority { get; set; }

		public string CreatedByUserId { get; set; }

		public DateTime CreatedOn { get; set; }

		/// <summary>Set when the need transitions to Met.</summary>
		public string MetByUserId { get; set; }

		/// <summary>Set when the need transitions to Met.</summary>
		public DateTime? MetOn { get; set; }

		public int SortOrder { get; set; }

		/// <summary>Change cursor for offline delta sync + last-write-wins; stamped on every write.</summary>
		public DateTime? ModifiedOn { get; set; }

		[NotMapped]
		public string TableName => "IncidentNeeds";

		[NotMapped]
		public string IdName => "IncidentNeedId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return IncidentNeedId; }
			set { IncidentNeedId = (string)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// Append-only audit row for one fulfillment change on an <see cref="IncidentNeed"/>: status and/or
	/// fill-quantity transition, the caller's note ("Engine 1 from mutual aid"), who made it, and when.
	/// </summary>
	public class IncidentNeedUpdate : IEntity
	{
		public string IncidentNeedUpdateId { get; set; }

		public string IncidentNeedId { get; set; }

		public string IncidentCommandId { get; set; }

		public int DepartmentId { get; set; }

		public int CallId { get; set; }

		/// <summary>Maps to <see cref="IncidentNeedStatus"/>.</summary>
		public int PreviousStatus { get; set; }

		/// <summary>Maps to <see cref="IncidentNeedStatus"/>.</summary>
		public int NewStatus { get; set; }

		public int PreviousQuantityFulfilled { get; set; }

		public int NewQuantityFulfilled { get; set; }

		/// <summary>Caller-supplied context for the change (which resource filled it, why it was called off, ...).</summary>
		public string Note { get; set; }

		public string CreatedByUserId { get; set; }

		/// <summary>Resolved display name for CreatedByUserId; filled at read time, never persisted.</summary>
		[NotMapped]
		public string CreatedByUserName { get; set; }

		public DateTime CreatedOn { get; set; }

		[NotMapped]
		public string TableName => "IncidentNeedUpdates";

		[NotMapped]
		public string IdName => "IncidentNeedUpdateId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return IncidentNeedUpdateId; }
			set { IncidentNeedUpdateId = (string)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "CreatedByUserName" };
	}
}
