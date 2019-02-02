using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	[Table("ScheduledTaskLogs")]
	public class ScheduledTaskLog : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int ScheduledTaskLogId { get; set; }

		[Required]
		public int ScheduledTaskId { get; set; }

		public DateTime RunDate { get; set; }

		public bool Successful { get; set; }

		[MaxLength(3000)]
		public string Data { get; set; }

		[ForeignKey("ScheduledTaskId")]
		public virtual ScheduledTask ScheduledTask { get; set; }

		[NotMapped]
		public object Id
		{
			get { return ScheduledTaskLogId; }
			set { ScheduledTaskLogId = (int)value; }
		}
	}
}