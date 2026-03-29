using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Newtonsoft.Json;

namespace Resgrid.Model
{
	public class CalendarItemCheckIn : IEntity
	{
		public string CalendarItemCheckInId { get; set; }

		public int DepartmentId { get; set; }

		public int CalendarItemId { get; set; }

		public string UserId { get; set; }

		public DateTime CheckInTime { get; set; }

		public DateTime? CheckOutTime { get; set; }

		public string CheckInByUserId { get; set; }

		public string CheckOutByUserId { get; set; }

		public bool IsManualOverride { get; set; }

		public string CheckInNote { get; set; }

		public string CheckOutNote { get; set; }

		public string CheckInLatitude { get; set; }

		public string CheckInLongitude { get; set; }

		public string CheckOutLatitude { get; set; }

		public string CheckOutLongitude { get; set; }

		public DateTime Timestamp { get; set; }

		[NotMapped]
		public string TableName => "CalendarItemCheckIns";

		[NotMapped]
		public string IdName => "CalendarItemCheckInId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		[JsonIgnore]
		public object IdValue
		{
			get { return CalendarItemCheckInId; }
			set { CalendarItemCheckInId = (string)value; }
		}

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };

		public TimeSpan? GetDuration()
		{
			if (CheckOutTime.HasValue)
				return CheckOutTime.Value - CheckInTime;

			return null;
		}
	}
}
