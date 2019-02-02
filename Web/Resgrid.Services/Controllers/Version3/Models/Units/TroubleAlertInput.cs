using ProtoBuf;
using System.Collections.Generic;

namespace Resgrid.Web.Services.Controllers.Version3.Models.Units
{
	[ProtoContract]
	public class TroubleAlertInput
	{
		[ProtoMember(1)]
		public int UnitId { get; set; }

		[ProtoMember(2)]
		public int? CallId { get; set; }

		[ProtoMember(3)]
		public string UserId { get; set; }

		[ProtoMember(4)]
		public string Latitude { get; set; }

		[ProtoMember(5)]
		public string Longitude { get; set; }

		[ProtoMember(6)]
		public List<Role> Roles { get; set; }
	}
}
