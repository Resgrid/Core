using System.Collections.Generic;
using ProtoBuf;

namespace Resgrid.Model
{
	[ProtoContract]
	public class PersonnelListStatusOrder
	{
		[ProtoMember(1)]
		public int Weight { get; set; }

		[ProtoMember(2)]
		public int StatusId { get; set; }
	}

	[ProtoContract]
	public class PersonnelListStatusOrderSetting
	{
		[ProtoMember(1)]
		public List<PersonnelListStatusOrder> Orders { get; set; }
	}
}
