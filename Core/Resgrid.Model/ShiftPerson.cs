using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;
using Resgrid.Model.Identity;

namespace Resgrid.Model
{
	[Table("ShiftPersons")]
	public class ShiftPerson : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int ShiftPersonId { get; set; }

		[Required]
		[ForeignKey("Shift"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int ShiftId { get; set; }

		[JsonIgnore]
		public virtual Shift Shift { get; set; }

		[Required]
		[ForeignKey("User"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public string UserId { get; set; }

		[JsonIgnore]
		public virtual IdentityUser User { get; set; }

		[ForeignKey("Group"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int? GroupId { get; set; }

		[JsonIgnore]
		public virtual DepartmentGroup Group { get; set; }

		[NotMapped]
		public object IdValue
		{
			get { return ShiftPersonId; }
			set { ShiftPersonId = (int)value; }
		}

		[NotMapped]
		public string TableName => "ShiftPersons";

		[NotMapped]
		public string IdName => "ShiftPersonId";

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "TableName", "IdName", "Shift", "User", "Group" };
	}
}
