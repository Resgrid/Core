using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{
	[Table("UnitLogs")]
	public class UnitLog : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int UnitLogId { get; set; }

		[Required]
		public int UnitId { get; set; }

		public DateTime Timestamp { get; set; }

		[Required]
		[MaxLength(4000)]
		public string Narrative { get; set; }

		[ForeignKey("UnitId")]
		public virtual Unit Unit { get; set; }

		[NotMapped]
		public object Id
		{
			get { return UnitLogId; }
			set { UnitLogId = (int)value; }
		}
	}
}
