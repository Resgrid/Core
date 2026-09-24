using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;
using Resgrid.Model.Helpers;

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

		/// <summary>
		/// Department-local wall-clock start of this day's shift (Day plus the shift's StartTime; midnight when the shift has
		/// no start time). Needs <see cref="Shift"/> loaded.
		/// </summary>
		[NotMapped]
		public DateTime Start
		{
			get { return ShiftTimeWindow.GetWindow(Day, Shift?.StartTime, Shift?.EndTime, Shift?.Hours).Start; }
		}

		/// <summary>
		/// Department-local wall-clock end of this day's shift. An end time at or before the start time runs into the next
		/// day (a 19:00 to 07:00 night shift); a shift with no end time runs for its Hours, or a full day.
		/// </summary>
		[NotMapped]
		public DateTime End
		{
			get { return ShiftTimeWindow.GetWindow(Day, Shift?.StartTime, Shift?.EndTime, Shift?.Hours).End; }
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
