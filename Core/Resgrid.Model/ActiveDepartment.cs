using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ProtoBuf;

namespace Resgrid.Model
{
	[Table("ActiveDepartments")]

	public class ActiveDepartment
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.None)]
		[ProtoMember(1)]
		public string UserId { get; set; }

		[Required]
		[ProtoMember(2)]
		public int ActiveDepartmentId { get; set; }
	}
}
