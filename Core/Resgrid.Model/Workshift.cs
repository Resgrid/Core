using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	/// <summary>
	/// A static workshifts
	/// </summary>
	/// <remarks>
	/// Workshifts are a collection of static shifts that are assigned to a department and differ
	/// from normal Shifts in that they are a set time period (start and end) with static assignments
	/// of units, persons or groups. The normal shifts system will be used for recurring shifts.
	/// </remarks>
	public class Workshift : IEntity
	{
		public string WorkshiftId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		[JsonIgnore]
		public virtual Department Department { get; set; }

		public int Type { get; set; }

		[Required]
		public string Name { get; set; }

		[Required]
		public string Color { get; set; }

		public string Description { get; set; }

		public DateTime Start { get; set; }
		
		public DateTime End { get; set; }

		public ICollection<WorkshiftDay> Days { get; set; }

		public ICollection<WorkshiftEntity> Entities { get; set; }

		public ICollection<WorkshiftFill> Fills { get; set; }

		public DateTime AddedOn { get; set; }

		public string AddedById { get; set; }

		public DateTime? DeletedOn { get; set; }

		public string DeletedById { get; set; }

		[NotMapped]
		[JsonIgnore]public object IdValue
		{
			get { return WorkshiftId; }
			set { WorkshiftId = (string)value; }
		}

		[NotMapped]
		public string TableName => "Workshifts";

		[NotMapped]
		public string IdName => "WorkshiftId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Department", "Days", "Entities", "Fills" };

		
	}
}
