using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Resgrid.Model
{

	[Table("LogUnits")]
	public class LogUnit : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int LogUnitId { get; set; }

		[ForeignKey("Log"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int LogId { get; set; }

		public virtual Log Log { get; set; }


		[ForeignKey("Unit"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int UnitId { get; set; }

		public virtual Unit Unit { get; set; }

		public DateTime? Dispatched { get; set; }

		public DateTime? Enroute { get; set; }

		public DateTime? OnScene { get; set; }

		public DateTime? Released { get; set; }

		public DateTime? InQuarters { get; set; }

		[NotMapped]
		public object Id
		{
			get { return LogUnitId; }
			set { LogUnitId = (int)value; }
		}
	}
}
