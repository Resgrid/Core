using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>
	/// One requested Resgrid entity under an Entity-category <see cref="IncidentNeed"/>: a unit, user,
	/// role, or group command asked for. The entity is added to the call and dispatched individually
	/// ("requested by command") when the need is created.
	/// </summary>
	public class IncidentNeedEntity : IEntity
	{
		public string IncidentNeedEntityId { get; set; }

		public string IncidentNeedId { get; set; }

		public string IncidentCommandId { get; set; }

		public int DepartmentId { get; set; }

		public int CallId { get; set; }

		/// <summary>Maps to <see cref="NeedEntityKind"/>.</summary>
		public int EntityKind { get; set; }

		/// <summary>UnitId / UserId / PersonnelRoleId / DepartmentGroupId depending on the kind.</summary>
		public string EntityId { get; set; }

		/// <summary>Display-name snapshot at request time (unit name, user full name, role/group name).</summary>
		public string EntityName { get; set; }

		/// <summary>When the individual dispatch for this entity was queued; null when dispatch failed.</summary>
		public DateTime? DispatchedOn { get; set; }

		public string CreatedByUserId { get; set; }

		public DateTime CreatedOn { get; set; }

		[NotMapped]
		public string TableName => "IncidentNeedEntities";

		[NotMapped]
		public string IdName => "IncidentNeedEntityId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return IncidentNeedEntityId; }
			set { IncidentNeedEntityId = (string)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
