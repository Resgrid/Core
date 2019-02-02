using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Entity.ModelConfiguration;
using System.Linq;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model.Helpers;

namespace Resgrid.Model
{
	[Table("Shifts")]
	public class Shift : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int ShiftId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		[JsonIgnore]
		public virtual Department Department { get; set; }

		public string Name { get; set; }
		public string Code { get; set; }
		public int ScheduleType { get; set; }
		public int AssignmentType { get; set; }
		public string Color { get; set; }
		public DateTime StartDay { get; set; }
		public string StartTime { get; set; }
		public string EndTime { get; set; }
		public int? Hours { get; set; }
		public bool? AllowPartials { get; set; }
		public bool? RequireApproval { get; set; }
		public virtual ICollection<ShiftGroup> Groups { get; set; }
		public virtual ICollection<ShiftDay> Days { get; set; }
		public virtual ICollection<ShiftPerson> Personnel { get; set; }
		public virtual ICollection<ShiftAdmin> Admins { get; set; }
		public virtual ICollection<ShiftSignup> Signups { get; set; }

		[NotMapped]
		public object Id
		{
			get { return ShiftId; }
			set { ShiftId = (int)value; }
		}

		public ShiftDay GetShiftDayforDateTime(DateTime timestamp)
		{
			if (Days != null && Days.Any() && Department != null)
			{
				var shiftStart = StartTime;

				if (String.IsNullOrWhiteSpace(shiftStart))
					shiftStart = "12:00 AM";

				var shiftDays = from sd in Days
												let shiftDayTime = DateTimeHelpers.ConvertStringTime(shiftStart, sd.Day, Department.Use24HourTime.GetValueOrDefault())
												let shiftDayToCheck = timestamp.TimeConverter(Department)
												where shiftDayTime == shiftDayToCheck.Within(TimeSpan.FromMinutes(15))
												select sd;

				if (shiftDays != null)
					return shiftDays.FirstOrDefault();
			}

			return null;
		}

		public ShiftDay GetNextShiftDayforDateTime(DateTime timestamp)
		{
			if (Days != null && Days.Any() && Department != null)
			{
				var shiftStart = StartTime;

				if (String.IsNullOrWhiteSpace(shiftStart))
					shiftStart = "12:00 AM";

				var shiftDays = from sd in Days
												let shiftDayTime = DateTimeHelpers.ConvertStringTime(shiftStart, sd.Day, Department.Use24HourTime.GetValueOrDefault())
												let shiftDayToCheck = timestamp.TimeConverter(Department)
												where shiftDayTime == shiftDayToCheck.Within(TimeSpan.FromMinutes(15))
												select sd;

				if (shiftDays != null && shiftDays.Any())
				{
					var day = shiftDays.FirstOrDefault();

					return Days.FirstOrDefault(x => x.ShiftDayId > day.ShiftDayId);
				}
			}

			return null;
		}
	}

	public class Shift_Mapping : EntityTypeConfiguration<Shift>
	{
		public Shift_Mapping()
		{
			this.HasRequired(t => t.Department).WithMany().HasForeignKey(t => t.DepartmentId).WillCascadeOnDelete(false);
		}
	}
}