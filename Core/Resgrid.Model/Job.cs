using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	[Table("Jobs")]
	public class Job : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int JobId { get; set; }

		public int JobType { get; set; }

		public int CheckInterval { get; set; }

		public DateTime? StartTimestamp { get; set; }

		public DateTime? LastCheckTimestamp { get; set; }

		public bool? DoRestart { get; set; }

		public DateTime? RestartRequestedTimestamp { get; set; }

		public DateTime? LastResetTimestamp { get; set; }

		[NotMapped]
		public object Id
		{
			get { return JobId; }
			set { JobId = (int)value; }
		}
	}
}
