using ProtoBuf;

namespace Resgrid.Model.Events
{
	[ProtoContract]
	public class PushRegisterionEvent
	{
		[ProtoMember(1)]
		public int PushUriId { get; set; }

		[ProtoMember(2)]
		public string UserId { get; set; }

		[ProtoMember(3)]
		public int PlatformType { get; set; }

		[ProtoMember(4)]
		public string DeviceId { get; set; }

		[ProtoMember(5)]
		public int UnitId { get; set; }

		[ProtoMember(6)]
		public string Uuid { get; set; }

		[ProtoMember(7)]
		public int DepartmentId { get; set; }

		[ProtoMember(8)]
		public string PushLocation { get; set; }
	}
}
