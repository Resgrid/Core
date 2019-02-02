using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;
using Microsoft.AspNet.Identity.EntityFramework6;

namespace Resgrid.Model
{
	[Table("ShiftGroupAssignments")]
	public class ShiftGroupAssignment : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int ShiftGroupAssignmentId { get; set; }

		[Required]
		[ForeignKey("ShiftGroup"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int ShiftGroupId { get; set; }

		public virtual ShiftGroup ShiftGroup { get; set; }

		[Required]
		[ForeignKey("User"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public string UserId { get; set; }

		[JsonIgnore]
		public virtual IdentityUser User { get; set; }

		public bool Assigned { get; set; }

		public DateTime Timestamp { get; set; }

		public Guid AssignedByUserId { get; set; }

		public DateTime ShiftDay { get; set; }

		[NotMapped]
		public object Id
		{
			get { return ShiftGroupAssignmentId; }
			set { ShiftGroupAssignmentId = (int) value; }
		}
	}
}