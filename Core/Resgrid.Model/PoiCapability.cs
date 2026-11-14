using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>
	/// A capability a department's points of interest can offer (M0315): "Cath Lab", "Stroke Center", "Trauma Center" for
	/// hospitals, and equally "Pet friendly" for a shelter or "Night landing" for a helipad. The department manages the list;
	/// POIs are tagged through <see cref="PoiCapabilityAssignment"/>, so a dispatcher can find the nearest destination that
	/// offers what a patient needs, and a destination can report one capability unavailable (<see cref="PoiAvailability"/>).
	/// No foreign keys: capability and POI deletes and the department purge (UnlinkedDepartmentCleanup) remove the rows.
	/// </summary>
	[Table("PoiCapabilities")]
	public class PoiCapability : IEntity
	{
		public const int MaxNameLength = 100;

		[Key]
		[Required]
		public string PoiCapabilityId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		[Required]
		[MaxLength(MaxNameLength)]
		public string Name { get; set; }

		/// <summary>Display order in lists and filters (ascending, then by name).</summary>
		public int Sort { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return PoiCapabilityId; }
			set { PoiCapabilityId = value?.ToString(); }
		}

		[NotMapped]
		public string TableName => "PoiCapabilities";

		[NotMapped]
		public string IdName => "PoiCapabilityId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>
	/// Tags one POI with one of the department's <see cref="PoiCapability"/> entries (M0315). DepartmentId is carried so a
	/// department's tags load in one query and the department purge sweeps them.
	/// </summary>
	[Table("PoiCapabilityAssignments")]
	public class PoiCapabilityAssignment : IEntity
	{
		[Key]
		[Required]
		public string PoiCapabilityAssignmentId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		[Required]
		public int PoiId { get; set; }

		[Required]
		public string PoiCapabilityId { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return PoiCapabilityAssignmentId; }
			set { PoiCapabilityAssignmentId = value?.ToString(); }
		}

		[NotMapped]
		public string TableName => "PoiCapabilityAssignments";

		[NotMapped]
		public string IdName => "PoiCapabilityAssignmentId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}
}
