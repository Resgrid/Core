using System.Collections.Generic;
using System.Linq;
using Resgrid.Model.Identity;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Orders
{
	public class FillOrderView
	{
		public Department Department { get; set; }
		public ResourceOrder Order { get; set; }
		public ResourceOrderFill Fill { get; set; }
		public Dictionary<string, string> Users;
		public int Count { get; set; }

		public FillOrderView()
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
