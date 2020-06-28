using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Model;
using Resgrid.Model.Identity;

namespace Resgrid.Web.Areas.User.Models.DistributionLists
{
	public class NewListView : BaseUserModel
	{
		public string Message { get; set; }
		public DistributionList List { get; set; }
		public List<IdentityUser> Users { get; set; }
		public DistributionListTypes Type { get; set; }
		public SelectList ListTypes { get; set; }
	}
}