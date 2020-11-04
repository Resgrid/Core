using ProtoBuf;

namespace Resgrid.Web.Eventing.Hubs.Models
{
	[ProtoContract]
	public class ConnectedUser
	{
		[ProtoMember(1)]
		public string ConnectionId { get; set; }

		[ProtoMember(2)]
		public string Identifier { get; set; } // UserId or UnitId

		[ProtoMember(3)]
		public int DepartmentId { get; set; }

		[ProtoMember(4)]
		public string Name { get; set; }

		[ProtoMember(5)]
		public int Type { get; set; } // 0 User, 1 Dispatch, 2 Unit

		[ProtoMember(6)]
		public string Data { get; set; }
	}
}
