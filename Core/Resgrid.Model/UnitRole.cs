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

		[NotMapped]
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
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "TableName", "IdName", "Unit" };
	}
}
