using Newtonsoft.Json;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	[Table("CalendarItemTypes")]
	public class CalendarItemType: IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int CalendarItemTypeId { get; set; }

		[Required]
		[ForeignKey("Department"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int DepartmentId { get; set; }

		public virtual Department Department { get; set; }

		[Required]
		public string Name { get; set; }

		public string Color { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return CalendarItemTypeId; }
			set { CalendarItemTypeId = (int)value; }
		}

		[NotMapped]
		public string TableName => "CalendarItemTypes";

		[NotMapped]
		public string IdName => "CalendarItemTypeId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Department" };
	}
}
