using ProtoBuf;

namespace Resgrid.Model
{
	[ProtoContract]
	public class DepartmentLimits
	{
		[ProtoMember(1)]
		public int PersonnelLimit { get; set; }

		[ProtoMember(2)]
		public int UnitsLimit { get; set; }

		[ProtoMember(3)]
		public int EntityTotal { get; set; }

		[ProtoMember(4)]
		public int PersonnelCount { get; set; }

		[ProtoMember(5)]
		public int UnitsCount { get; set; }

		[ProtoMember(6)]
		public bool IsEntityPlan { get; set; }

		public int GetCurrentEntityTotal()
		{
			return PersonnelCount + UnitsCount;
		}
	}
}
