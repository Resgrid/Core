using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Resgrid.Model.Identity;
using System.Linq;
using ProtoBuf;
using Resgrid.Framework;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	[ProtoContract]
	[Table("ScheduledTasks")]
	public class ScheduledTask : IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		[ProtoMember(1)]
		public int ScheduledTaskId { get; set; }

		[Required]
		[ProtoMember(2)]
		public string UserId { get; set; }

		[ProtoMember(3)]
		public int ScheduleType { get; set; }

		[ProtoMember(4)]
		public DateTime? SpecifcDate { get; set; }

		[ProtoMember(5)]
		public bool Sunday { get; set; }

		[ProtoMember(6)]
		public bool Monday { get; set; }

		[ProtoMember(7)]
		public bool Tuesday { get; set; }

		[ProtoMember(8)]
		public bool Wednesday { get; set; }

		[ProtoMember(9)]
		public bool Thursday { get; set; }

		[ProtoMember(10)]
		public bool Friday { get; set; }

		[ProtoMember(11)]
		public bool Saturday { get; set; }

		[MaxLength(50)]
		[ProtoMember(12)]
		public string Time { get; set; }

		[ProtoMember(13)]
		public bool Active { get; set; }

		[ProtoMember(14)]
		public int TaskType { get; set; }

		[MaxLength(3000)]
		[ProtoMember(15)]
		public string Data { get; set; }

		[ProtoMember(16)]
		public DateTime? AddedOn { get; set; }

		[ForeignKey("UserId")]
		public virtual IdentityUser User { get; set; }

		[ProtoMember(17)]
		public string Note { get; set; }

		[ProtoMember(18)]
		public int DepartmentId { get; set; }

		//[ForeignKey("DepartmentId")]
		//public virtual Department Department { get; set; }

		[NotMapped]
		[ProtoMember(19)]
		public string DepartmentTimeZone { get; set; }

		[NotMapped]
		[ProtoMember(20)]
		public string UserEmailAddress { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return ScheduledTaskId; }
			set { ScheduledTaskId = (int)value; }
		}

		[NotMapped]
		public string TableName => "ScheduledTasks";

		[NotMapped]
		public string IdName => "ScheduledTaskId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "User", "DepartmentTimeZone", "UserEmailAddress" };

		public List<DayOfWeek> GetDaysOfWeek()
		{
			var days = new List<DayOfWeek>();

			if (Sunday)
				days.Add(DayOfWeek.Sunday);

			if (Monday)
				days.Add(DayOfWeek.Monday);

			if (Tuesday)
				days.Add(DayOfWeek.Tuesday);

			if (Wednesday)
				days.Add(DayOfWeek.Wednesday);

			if (Thursday)
				days.Add(DayOfWeek.Thursday);

			if (Friday)
				days.Add(DayOfWeek.Friday);

			if (Saturday)
				days.Add(DayOfWeek.Saturday);

			return days;
		}

		public DateTime? WhenShouldJobBeRun(DateTime currentLocalTime)
		{
			if (ScheduleType == (int)ScheduleTypes.SpecifcDateTime)
			{
				if (SpecifcDate.HasValue)
					return SpecifcDate.Value;
			}
			else
			{
				if (GetDaysOfWeek().Any(x => x == currentLocalTime.DayOfWeek))
				{
					bool am = Time.ToUpper().Contains("AM");
					int hour = 0;
					int minute = 0;

					String[] timeParts = Time.Split(char.Parse(":"));

					if (timeParts.Count() == 2)
					{
						var stringHour = StringHelpers.GetNumbers(timeParts[0]);
						var stringMinute = StringHelpers.GetNumbers(timeParts[1]);

						if (String.IsNullOrWhiteSpace(stringHour) || String.IsNullOrWhiteSpace(stringMinute))
							return null;

						hour = int.Parse(stringHour.Trim());
						minute = int.Parse(stringMinute.Trim());
					}
					else if (timeParts.Count() == 1)
					{
						var stringHour = StringHelpers.GetNumbers(timeParts[0]);

						if (String.IsNullOrWhiteSpace(stringHour))
							return null;

						hour = int.Parse(stringHour);

						if (!Time.ToUpper().Contains("AM") || !Time.ToUpper().Contains("PM"))
							if (hour < 12)
								am = true;
					}

					int adjustedHours = 0;

					if (hour == 12)
					{
						if (!am)
							adjustedHours = 12;
						else
							adjustedHours = 0;
					}
					else
					{
						if (!am)
							adjustedHours = 12 + hour;
						else
							adjustedHours = hour;
					}

					int dayAdjust = 0;

					/* If the current time is at least 11:00 PM and the time we're evaluating is for
					 * midnight we have to add a day so that when we do the time math it will work out
					 */
					if (currentLocalTime.Hour == 23 && am && hour == 12)
						dayAdjust = 1;

					var adjustedTime = currentLocalTime.AddHours(adjustedHours - currentLocalTime.Hour).AddMinutes(minute - currentLocalTime.Minute).AddSeconds(-currentLocalTime.Second).AddDays(dayAdjust);

					return adjustedTime;
				}
			}

			return null;
		}
	}
}
