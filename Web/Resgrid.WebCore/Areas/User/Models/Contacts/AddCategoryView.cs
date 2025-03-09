using Resgrid.Model;
using System.Collections.Generic;

namespace Resgrid.Web.Areas.User.Models.Contacts
{
	public class AddCategoryView
	{
		public Department Department { get; set; }
		public ContactCategory Category { get; set; }

		public AddCategoryView()
		{
			Category = new ContactCategory();
		}
	}
}
