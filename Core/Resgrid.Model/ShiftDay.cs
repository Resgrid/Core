using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	[Table("ShiftDays")]
	public class ShiftDay : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int ShiftDayId { get; set; }

		[Required]
		[ForeignKey("Shift"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int ShiftId { get; set; }

		[JsonIgnore]
		public virtual Shift Shift { get; set; }

		public DateTime Day { get; set; }

		public bool? Processed { get; set; }

		[NotMapped]
		public DateTime Start
		{
			get
			{
				if (Shift != null && !String.IsNullOrWhiteSpace(Shift.StartTime))
				{
					return DateTime.Parse($"{Day.Month}/{Day.Day}/{Day.Year} " + Shift.StartTime);
				}

				return Day;
			}
		}

		[NotMapped]
		public DateTime End
		{
			get
			{
				if (Shift != null && !String.IsNullOrWhiteSpace(Shift.EndTime))
				{
					return DateTime.Parse($"{Day.Month}/{Day.Day}/{Day.Year} " + Shift.EndTime);
				}

				return DateTime.Parse($"{Day.Month}/{Day.Day}/{Day.Year} 23:59:59");
			}
		}

		[NotMapped]
		[JsonIgnore]public object IdValue
		{
			get { return ShiftDayId; }
			set { ShiftDayId = (int)value; }
		}

		[NotMapped]
		public string TableName => "ShiftDays";

		[NotMapped]
		public string IdName => "ShiftDayId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Shift" };
	}
}
