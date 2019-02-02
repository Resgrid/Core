using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

namespace Resgrid.Model
{
	[Table("PersonnelRoles")]
	public class PersonnelRole : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int PersonnelRoleId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		[ForeignKey("DepartmentId")]
		public Department Department { get; set; }

		[Required]
		[MaxLength(250)]
		public string Name { get; set; }

		[MaxLength(3000)]
		public string Description { get; set; }

		public virtual ICollection<PersonnelRoleUser> Users { get; set; }

		[NotMapped]
		public object Id
		{
			get { return PersonnelRoleId; }
			set { PersonnelRoleId = (int)value; }
		}

		public bool IsUserInRole(string userId)
		{
			if (Users != null && Users.Count > 0)
			{
				return Users.Any(x => x.UserId == userId);
			}

			return false;
		}
	}
}