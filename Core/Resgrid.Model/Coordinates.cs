using ProtoBuf;

namespace Resgrid.Model
{
	[ProtoContract]
	public class Coordinates
	{
		[ProtoMember(1)]
		public double? Longitude { get; set; }
		[ProtoMember(2)]
		public double? Latitude { get; set; }
	}
}