using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Entity.ModelConfiguration;
using Microsoft.AspNet.Identity.EntityFramework6;
using ProtoBuf;

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
		public object Id
		{
			get { return CalendarItemAttendeeId; }
			set { CalendarItemAttendeeId = (int)value; }
		}
	}

	public class CalendarItemAttendee_Mapping : EntityTypeConfiguration<CalendarItemAttendee>
	{
		public CalendarItemAttendee_Mapping()
		{
			this.HasRequired(t => t.User).WithMany().HasForeignKey(t => t.UserId).WillCascadeOnDelete(false);
		}
	}
}