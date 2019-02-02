using System;
using ProtoBuf;

namespace Resgrid.Model.Custom
{
	[ProtoContract]
	public class ValidateUserForDepartmentResult
	{
		[ProtoMember(1)]
		public string UserId { get; set; }

		[ProtoMember(2)]
		public bool? IsDisabled { get; set; }

		[ProtoMember(5)]
		public bool? IsDeleted { get; set; }

		[ProtoMember(3)]
		public int DepartmentId { get; set; }

		[ProtoMember(4)]
		public string Code { get; set; }
	}
}