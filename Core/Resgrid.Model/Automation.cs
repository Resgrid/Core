using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	[Table("Automations")]
	public class Automation : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int AutomationId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		[ForeignKey("DepartmentId")]
		public virtual Department Department { get; set; }

		public int AutomationType { get; set; }
		public bool IsDisabled { get; set; }
		public string TargetType { get; set; }
		public int? GroupId { get; set; }
		public string Data { get; set; }
		public string CreatedByUserId { get; set; }
		public DateTime CreatedOn { get; set; }

		[NotMapped]
		public object Id
		{
			get { return AutomationId; }
			set { AutomationId = (int)value; }
		}
	}
}
