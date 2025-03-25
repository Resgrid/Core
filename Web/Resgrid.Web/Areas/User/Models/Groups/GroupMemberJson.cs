using System;

namespace Resgrid.Web.Areas.User.Models.Groups
{
	public class GroupMemberJson
	{
		public int GroupMemberId { get; set; }
		public int DepartmentGroupId { get; set; }
		public string UserId { get; set; }
		public bool IsAdmin { get; set; }
		public string Name { get; set; }
	}
}