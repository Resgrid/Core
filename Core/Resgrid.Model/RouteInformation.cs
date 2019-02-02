using System;
using ProtoBuf;

namespace Resgrid.Model
{
	[ProtoContract]
	public class RouteInformation
	{
		[ProtoMember(1)]
		public string Name { get; set; }

		[ProtoMember(2)]
		public string TimeString { get; set; }

		[ProtoMember(3)]
		public double Seconds{ get; set; }

		[ProtoMember(4)]
		public DateTime ProcessedOn { get; set; }

		[ProtoMember(5)]
		public bool Successful { get; set; }

		[ProtoMember(6)]
		public int DistanceInMeters { get; set; }
	}
}
