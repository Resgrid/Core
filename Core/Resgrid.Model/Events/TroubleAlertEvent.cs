using ProtoBuf;
using System;
using System.Collections.Generic;

namespace Resgrid.Model.Events
{
	[ProtoContract]
	public class TroubleAlertEvent
	{
		[ProtoMember(1)]
		public int UnitId { get; set; }

		[ProtoMember(2)]
		public int? CallId { get; set; }

		[ProtoMember(3)]
		public string UserId { get; set; }

		[ProtoMember(4)]
		public int? DepartmentId { get; set; }

		[ProtoMember(5)]
		public DateTime? TimeStamp { get; set; }

		[ProtoMember(6)]
		public string Latitude { get; set; }

		[ProtoMember(7)]
		public string Longitude { get; set; }

		[ProtoMember(8)]
		public List<TroubleAlertRole> Roles { get; set; }
	}

	[ProtoContract]
	public class TroubleAlertRole
	{
		[ProtoMember(1)]
		public string UserId { get; set; }

		[ProtoMember(2)]
		public int RoleId { get; set; }

		[ProtoMember(3)]
		public string UserFullName { get; set; }

		[ProtoMember(4)]
		public string RoleName { get; set; }
	}
}
