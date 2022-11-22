using ProtoBuf;
using System;

namespace Resgrid.Model.Events
{
	[ProtoContract]
	public class PersonnelLocationEvent
	{
		[ProtoMember(1)]
		public string EventId { get; set; }

		[ProtoMember(2)]
		public int PersonnelLocationId { get; set; }

		[ProtoMember(3)]
		public int DepartmentId { get; set; }

		[ProtoMember(4)]
		public string UserId { get; set; }

		[ProtoMember(5)]
		public DateTime Timestamp { get; set; }

		[ProtoMember(6)]
		public decimal Latitude { get; set; }

		[ProtoMember(7)]
		public decimal Longitude { get; set; }

		[ProtoMember(8)]
		public decimal? Accuracy { get; set; }

		[ProtoMember(9)]
		public decimal? Altitude { get; set; }

		[ProtoMember(10)]
		public decimal? AltitudeAccuracy { get; set; }

		[ProtoMember(11)]
		public decimal? Speed { get; set; }

		[ProtoMember(12)]
		public decimal? Heading { get; set; }


		public PersonnelLocationEvent()
		{
			EventId = Guid.NewGuid().ToString();
		}
	}
}
