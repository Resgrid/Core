using System;
using System.Collections.Generic;
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
		public object IdValue
		{
			get { return ScheduledTaskLogId; }
			set { ScheduledTaskLogId = (int)value; }
		}

		[NotMapped]
		public string TableName => "ScheduledTaskLogs";

		[NotMapped]
		public string IdName => "ScheduledTaskLogId";

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "TableName", "IdName", "ScheduledTask" };
	}
}
