using Newtonsoft.Json;
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

		/// <summary>
		/// The department's uploaded icon for the role (M0286; <see cref="TypeIconKinds.PersonnelRole"/>), or null. Members'
		/// map pins use it, by <see cref="SortOrder"/>; set only through the type icon service.
		/// </summary>
		[MaxLength(64)]
		public string IconKey { get; set; }

		/// <summary>
		/// Position on the Roles page (M0286), lowest first; ties order by name. A person's map pin uses the icon of the
		/// first role they hold, in this order, that has one.
		/// </summary>
		public int SortOrder { get; set; }

		public virtual ICollection<PersonnelRoleUser> Users { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return PersonnelRoleId; }
			set { PersonnelRoleId = (int)value; }
		}

		[NotMapped]
		public string TableName => "PersonnelRoles";

		[NotMapped]
		public string IdName => "PersonnelRoleId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Department", "Users" };

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
