using Newtonsoft.Json;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	/// <summary>
	/// Minimum coverage a station should retain: at least MinimumAvailableCount of a
	/// unit type OR a personnel role available at/near the station. Exactly one of
	/// UnitTypeId / PersonnelRoleId is set. When the recommendation engine's move-up
	/// pass finds coverage below the minimum it emits move-up/backfill recommendations.
	/// </summary>
	[Table("StationCoverageRequirements")]
	public class StationCoverageRequirement : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int StationCoverageRequirementId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		[Required]
		public int DepartmentGroupId { get; set; }

		public virtual DepartmentGroup StationGroup { get; set; }

		public int? UnitTypeId { get; set; }

		public int? PersonnelRoleId { get; set; }

		[Required]
		public int MinimumAvailableCount { get; set; }

		/// <summary>
		/// ClosestUnit mode: availability is measured within this radius of the station's
		/// coordinates. Null = measure by station assignment/geofence (StationBased semantics).
		/// </summary>
		public int? RadiusMeters { get; set; }

		public bool IsEnabled { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return StationCoverageRequirementId; }
			set { StationCoverageRequirementId = (int)value; }
		}

		[NotMapped]
		public string TableName => "StationCoverageRequirements";

		[NotMapped]
		public string IdName => "StationCoverageRequirementId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "StationGroup" };
	}
}
