using System.Collections.Generic;
using System.Linq;
using Resgrid.Model.Identity;
using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Model;

namespace Resgrid.WebCore.Areas.User.Models.Orders
{
    public class OrderSetttingsView
    {
		public ResourceOrderSetting Settings { get; set; }

		public ResourceOrderVisibilites Visibility { get; set; }
		public SelectList OrderVisibilities { get; set; }

		public SelectList StaffingLevels { get; set; }
		public UserStateTypes UserStateTypes { get; set; }

		public Dictionary<string, string> Users;

	    public OrderSetttingsView()
	    {
			Users = new Dictionary<string, string>();
		}

		public void SetUsers(List<IdentityUser> users, List<PersonName> names)
		{
			foreach (var u in users)
			{
				var name = names.FirstOrDefault(x => x.UserId == u.UserId);

				if (name != null)
					Users.Add(u.UserId, name.Name);
			}
		}
	}
}
