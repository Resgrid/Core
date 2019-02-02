using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNet.Identity.EntityFramework6;

namespace Resgrid.Model
{
	[Table("UnitStateRoles")]
	public class UnitStateRole : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int UnitStateRoleId { get; set; }

		[Required]
		[ForeignKey("UnitState"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int UnitStateId { get; set; }

		public UnitState UnitState { get; set; }

		[MaxLength(250)]
		public string Role { get; set; }

		[Required]
		[ForeignKey("User"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public string UserId { get; set; }

		public virtual IdentityUser User { get; set; }

		[NotMapped]
		public object Id
		{
			get { return UnitStateRoleId; }
			set { UnitStateRoleId = (int)value; }
		}
	}
}
