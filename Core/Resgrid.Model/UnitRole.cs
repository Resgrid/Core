using Newtonsoft.Json;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	[Table("UnitRoles")]
	public class UnitRole : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int UnitRoleId { get; set; }

		[Required]
		public int UnitId { get; set; }

		[ForeignKey("UnitId")]
		public Unit Unit { get; set; }

		[Required]
		[MaxLength(250)]
		public string Name { get; set; }

		/// <summary>
		/// Optional <see cref="PersonnelRole"/> (a personnel qualification such as "Paramedic") that the
		/// person filling this unit role must hold. Null means the seat has no qualification requirement.
		/// </summary>
		public int? PersonnelRoleId { get; set; }

		/// <summary>
		/// When true and <see cref="PersonnelRoleId"/> is set, a user lacking that personnel role is
		/// blocked from being assigned to this unit role (a hard requirement). When false the requirement
		/// is "preferred": the assignment is allowed but the unit is reported as degraded.
		/// </summary>
		public bool PersonnelRoleRequired { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return UnitRoleId; }
			set { UnitRoleId = (int)value; }
		}

		[NotMapped]
		public string TableName => "UnitRoles";

		[NotMapped]
		public string IdName => "UnitRoleId";

		[NotMapped]
		public int IdType => 0;


		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Unit" };
	}
}
