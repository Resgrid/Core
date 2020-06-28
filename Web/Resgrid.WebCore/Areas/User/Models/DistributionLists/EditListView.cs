using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Model;
using Resgrid.Model.Identity;

namespace Resgrid.Web.Areas.User.Models.DistributionLists
{
	public class EditListView : BaseUserModel
	{
		public string Message { get; set; }
		public DistributionList List { get; set; }
		public List<IdentityUser> Users { get; set; }
		public DistributionListTypes Type { get; set; }
		public SelectList ListTypes { get; set; }

		public bool IsUserInList(string userId)
		{
			if (List == null || List.Members == null)
				return false;

			return List.Members.Any(x => x.UserId == userId);
		}
	}
}
