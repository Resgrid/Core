using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Resgrid.Model.Identity;
using ProtoBuf;
using System.Collections.Generic;

namespace Resgrid.Model
{
	[ProtoContract]
	[Table("CalendarItemAttendees")]
	public class CalendarItemAttendee: IEntity
	{
		[Key]
		[Required]
		[ProtoMember(1)]
		[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
		public int CalendarItemAttendeeId { get; set; }

		[Required]
		[ProtoMember(2)]
		[ForeignKey("CalendarItem"), DatabaseGenerated(DatabaseGeneratedOption.None)]
		public int CalendarItemId { get; set; }

		public virtual CalendarItem CalendarItem { get; set; }

		[Required]
		[ProtoMember(3)]
		public string UserId { get; set; }

		public virtual IdentityUser User { get; set; }

		[ProtoMember(4)]
		public int AttendeeType { get; set; }

		[ProtoMember(5)]
		public DateTime Timestamp { get; set; }

		[ProtoMember(6)]
		public string Note { get; set; }

		[NotMapped]
		public object IdValue
		{
			get { return CalendarItemAttendeeId; }
			set { CalendarItemAttendeeId = (int)value; }
		}

		[NotMapped]
		public string TableName => "CalendarItemAttendees";

		[NotMapped]
		public string IdName => "CalendarItemAttendeeId";

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "TableName", "IdName", "CalendarItem", "User" };
	}
}
