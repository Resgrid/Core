using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;
using Resgrid.Model.Identity;

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
		[JsonIgnore]
		public object IdValue
		{
			get { return UnitStateRoleId; }
			set { UnitStateRoleId = (int)value; }
		}

		[NotMapped]
		public string TableName => "UnitStateRoles";

		[NotMapped]
		public string IdName => "UnitStateRoleId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "UnitState", "User" };
	}
}
