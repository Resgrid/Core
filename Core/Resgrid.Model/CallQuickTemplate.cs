using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNet.Identity.EntityFramework6;

namespace Resgrid.Model
{
	[Table("CallQuickTemplates")]
	public class CallQuickTemplate : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int CallQuickTemplateId { get; set; }

		[Required]
		[ForeignKey("Department"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int DepartmentId { get; set; }

		public virtual Department Department { get; set; }

		public bool IsDisabled { get; set; }

		[Required]
		public string Name { get; set; }

		public string CallName { get; set; }
		public string CallNature { get; set; }
		public string CallType { get; set; }
		public int CallPriority { get; set; }

		[ForeignKey("CreatedByUser"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public string CreatedByUserId { get; set; }

		public virtual IdentityUser CreatedByUser { get; set; }

		public DateTime CreatedOn { get; set; }

		[NotMapped]
		public object Id
		{
			get { return CallQuickTemplateId; }
			set { CallQuickTemplateId = (int)value; }
		}
	}
}
