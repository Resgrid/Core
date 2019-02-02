using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	[Table("LogEntries")]
	public class LogEntry : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int id { get; set; }

		public DateTime TimeStamp { get; set; }
		public string Message { get; set; }
		public string level { get; set; }
		public string logger { get; set; }

		[NotMapped]
		public object Id
		{
			get { return id; }
			set { id = (int)value; }
		}
	}
}
