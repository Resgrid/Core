using System;
using ProtoBuf;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

namespace Resgrid.Model
{
	[ProtoContract]
	public class UserGroupRole
	{
		[ProtoMember(1)]
		public int? DepartmentGroupId { get; set; }

		[ProtoMember(2)]
		public string UserId { get; set; }

		[ProtoMember(3)]
		public string Roles { get; set;}

		[ProtoMember(4)]
		public string DepartmentGroupName { get; set; }

		[ProtoMember(5)]
		public string RoleNames { get; set; }

		//[ProtoMember(6)]
		public string Name => $"{FirstName} {LastName}";

		[ProtoMember(7)]
		public string FirstName {get;set;}

		[ProtoMember(8)]
		public string LastName {get; set; }

		[NotMapped]
		public List<int> RoleList
		{
			get
			{
				if (String.IsNullOrWhiteSpace(Roles))
					return new List<int>();

				return Roles.Split(char.Parse(",")).Select( x => int.Parse(x)).ToList();
			}
		}

		[NotMapped]
		public List<string> RoleNamesList
		{
			get
			{
				if (String.IsNullOrWhiteSpace(RoleNames))
					return new List<string>();

				return RoleNames.Split(char.Parse(",")).ToList();
			}
		}
	}
}
