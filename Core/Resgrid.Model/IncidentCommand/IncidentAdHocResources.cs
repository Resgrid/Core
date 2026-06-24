using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>
	/// An incident-scoped, ad-hoc unit created on the fly for resources not in Resgrid (e.g. a mutual-aid
	/// crew from a non-Resgrid agency, or a unit formed from on-scene personnel). Not a real department Unit.
	/// </summary>
	public class IncidentAdHocUnit : IEntity, IChangeTracked
	{
		public string IncidentAdHocUnitId { get; set; }

		public int DepartmentId { get; set; }

		public int CallId { get; set; }

		public string Name { get; set; }

		/// <summary>Optional reference to a department UnitType for classification.</summary>
		public int? UnitTypeId { get; set; }

		/// <summary>Free-text unit type (e.g. "Engine", "Ambulance") when no UnitTypeId applies.</summary>
		public string Type { get; set; }

		/// <summary>Name of the external (non-Resgrid) agency this resource belongs to, if any.</summary>
		public string ExternalAgencyName { get; set; }

		public string CreatedByUserId { get; set; }

		public DateTime CreatedOn { get; set; }

		public DateTime? ReleasedOn { get; set; }

		/// <summary>Change cursor for offline delta sync + last-write-wins; stamped on every write.</summary>
		public DateTime? ModifiedOn { get; set; }

		[NotMapped]
		public string TableName => "IncidentAdHocUnits";

		[NotMapped]
		public string IdName => "IncidentAdHocUnitId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return IncidentAdHocUnitId; }
			set { IncidentAdHocUnitId = (string)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// An incident-scoped, ad-hoc person created on the fly for resources not in Resgrid. May ride an ad-hoc
	/// (or real) unit for accountability via <see cref="RidingResourceKind"/> + <see cref="RidingResourceId"/>.
	/// </summary>
	public class IncidentAdHocPersonnel : IEntity, IChangeTracked
	{
		public string IncidentAdHocPersonnelId { get; set; }

		public int DepartmentId { get; set; }

		public int CallId { get; set; }

		public string Name { get; set; }

		/// <summary>Role / qualification (e.g. "Paramedic", "Firefighter").</summary>
		public string Role { get; set; }

		public string ExternalAgencyName { get; set; }

		public string Contact { get; set; }

		/// <summary>The kind of unit this person is riding for accountability (maps to <see cref="ResourceAssignmentKind"/>).</summary>
		public int RidingResourceKind { get; set; }

		/// <summary>Identifier of the unit this person is riding (ad-hoc unit id, real unit id, ...), or null.</summary>
		public string RidingResourceId { get; set; }

		public string CreatedByUserId { get; set; }

		public DateTime CreatedOn { get; set; }

		public DateTime? ReleasedOn { get; set; }

		/// <summary>Change cursor for offline delta sync + last-write-wins; stamped on every write.</summary>
		public DateTime? ModifiedOn { get; set; }

		[NotMapped]
		public string TableName => "IncidentAdHocPersonnel";

		[NotMapped]
		public string IdName => "IncidentAdHocPersonnelId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return IncidentAdHocPersonnelId; }
			set { IncidentAdHocPersonnelId = (string)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
