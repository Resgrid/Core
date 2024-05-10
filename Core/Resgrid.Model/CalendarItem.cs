using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Newtonsoft.Json;
using Resgrid.Framework;

namespace Resgrid.Model
{
	[Table("CalendarItems")]
	public class CalendarItem: IEntity
	{
		[Key]
		[Required]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int CalendarItemId { get; set; }

		[Required]
		[ForeignKey("Department"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int DepartmentId { get; set; }

		public virtual Department Department { get; set; }

		[Required]
		public string Title { get; set; }

		[Required]
		public DateTime Start { get; set; }

		[Required]
		public DateTime End { get; set; }

		public string StartTimezone { get; set; }

		public string EndTimezone { get; set; }

		public string Description { get; set; }

		public string RecurrenceId { get; set; }

		public string RecurrenceRule { get; set; }

		public string RecurrenceException { get; set; }

		public int ItemType { get; set; }

		public bool IsAllDay { get; set; }

		public string Location { get; set; }

		public int SignupType { get; set; }

		public int Reminder { get; set; }

		public bool LockEditing { get; set; }

		public string Entities { get; set; }

		public string RequiredAttendes { get; set; }

		public string OptionalAttendes { get; set; }

		public bool ReminderSent { get; set; }

		public string CreatorUserId { get; set; }

		public bool Public { get; set; }

		public bool IsV2Schedule { get; set; }

		public int RecurrenceType { get; set; } //RecurrenceTypes

		public DateTime? RecurrenceEnd { get; set; }

		public bool Sunday { get; set; }

		public bool Monday { get; set; }

		public bool Tuesday { get; set; }

		public bool Wednesday { get; set; }

		public bool Thursday { get; set; }

		public bool Friday { get; set; }

		public bool Saturday { get; set; }

		public int RepeatOnDay { get; set; } // 15th or 4th of every month

		public int RepeatOnWeek { get; set; } // 1st Week, 2nd Week, etc

		public int RepeatOnMonth { get; set; } // 1 = Jan, 2 = Feb, 3 = March, etc

		public virtual ICollection<CalendarItemAttendee> Attendees { get; set; }

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return CalendarItemId; }
			set { CalendarItemId = (int)value; }
		}

		
		[NotMapped]
		public string TableName => "CalendarItems";

		[NotMapped]
		public string IdName => "CalendarItemId";

		[NotMapped]
		public int IdType => 0;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName", "Attendees", "Department" };

		public bool IsUserAttending(string userId)
		{
			if (Attendees == null || Attendees.Count <= 0)
				return false;

			return Attendees.Any(x => x.UserId == userId);
		}

		public TimeSpan GetDifferenceBetweenStartAndEnd()
		{
			return End - Start;
		}

		public CalendarItem CreateRecurranceItem(DateTime start, DateTime end, string timeZone)
		{
			var item = new CalendarItem();
			item.DepartmentId = DepartmentId;
			item.RecurrenceId = CalendarItemId.ToString();
			item.Title = Title;

			/* Some detail here, ToUniversalTime was messing up around daylight savings time
			 * and would cause the conversion to loose or add an hour depending on the time zone
			 * and time of year. NodaTime doesn't have the issue or handles it correctly so made
			 * this change. Will need to start utilizing NodaTime in the system for all Tz converisons. -SJ
			 */
			item.Start = DateTimeHelpers.ConvertToUtc(start, timeZone);
			item.End = DateTimeHelpers.ConvertToUtc(end, timeZone);

			item.IsV2Schedule = true;
			item.Reminder = Reminder;
			item.Location = Location;
			item.LockEditing = true;
			item.Entities = Entities;
			item.Description = Description;
			item.RequiredAttendes = RequiredAttendes;
			item.OptionalAttendes = OptionalAttendes;
			item.CreatorUserId = CreatorUserId;
			item.IsAllDay = IsAllDay;
			item.ItemType = ItemType;
			item.SignupType = SignupType;
			item.Public = Public;
			item.StartTimezone = timeZone;
			item.EndTimezone = timeZone;

			return item;
		}

		public int GetMinutesForReminder()
		{
			switch (((CalendarItemReminderTypes)Reminder))
			{
				case CalendarItemReminderTypes.ZeroMinutes:
					return 0;
				case CalendarItemReminderTypes.FiveMinutes:
					return 5;
				case CalendarItemReminderTypes.TenMinutes:
					return 10;
				case CalendarItemReminderTypes.FifteenMinutes:
					return 15;
				case CalendarItemReminderTypes.ThirtyMinutes:
					return 30;
				case CalendarItemReminderTypes.OneHour:
					return 60;
				case CalendarItemReminderTypes.TwoHours:
					return 120;
				case CalendarItemReminderTypes.ThreeHours:
					return 180;
				case CalendarItemReminderTypes.FourHours:
					return 240;
				case CalendarItemReminderTypes.FiveHours:
					return 300;
				case CalendarItemReminderTypes.SixHours:
					return 360;
				case CalendarItemReminderTypes.SevenHours:
					return 420;
				case CalendarItemReminderTypes.EightHours:
					return 480;
				case CalendarItemReminderTypes.NineHours:
					return 540;
				case CalendarItemReminderTypes.TenHours:
					return 600;
				case CalendarItemReminderTypes.ElevenHours:
					return 660;
				case CalendarItemReminderTypes.TwelveHours:
					return 720;
				case CalendarItemReminderTypes.EighteenHours:
					return 1080;
				case CalendarItemReminderTypes.OneDay:
					return 1440;
				case CalendarItemReminderTypes.TwoDays:
					return 2880;
				case CalendarItemReminderTypes.ThreeDays:
					return 4320;
				case CalendarItemReminderTypes.FourDays:
					return 5760;
				case CalendarItemReminderTypes.OneWeek:
					return 10080;
				case CalendarItemReminderTypes.TwoWeeks:
					return 20160;
				default:
					return 1440;	// Default to 1 day
			}
		}
	}
}
