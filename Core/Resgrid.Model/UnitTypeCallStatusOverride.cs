using System.Collections.Generic;
using ProtoBuf;

namespace Resgrid.Model
{
	[ProtoContract]
	public class UnitTypeCallStatusOverride
	{
		public UnitTypeCallStatusOverride()
		{
			DispatchStatus = -1;
			ReleaseStatus = -1;
		}

		[ProtoMember(1)]
		public int UnitTypeId { get; set; }

		[ProtoMember(2)]
		public int DispatchStatus { get; set; }

		[ProtoMember(3)]
		public int ReleaseStatus { get; set; }
	}

	[ProtoContract]
	public class UnitTypeCallStatusOverrideSetting
	{
		public UnitTypeCallStatusOverrideSetting()
		{
			Overrides = new List<UnitTypeCallStatusOverride>();
		}

		[ProtoMember(1)]
		public List<UnitTypeCallStatusOverride> Overrides { get; set; }
	}
}
