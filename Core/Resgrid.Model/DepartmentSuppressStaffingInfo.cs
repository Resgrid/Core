using ProtoBuf;
using System.Collections.Generic;

namespace Resgrid.Model
{
	[ProtoContract]
	public class DepartmentSuppressStaffingInfo
	{
		[ProtoMember(1)]
		public bool EnableSupressStaffing { get; set; }

		[ProtoMember(2)]
		public List<int> StaffingLevelsToSupress { get; set; }

		public DepartmentSuppressStaffingInfo()
		{
			StaffingLevelsToSupress = new List<int>();
		}
	}
}
