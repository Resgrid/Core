using Resgrid.Model;
using System.Collections.Generic;

namespace Resgrid.Web.Areas.User.Models.Contacts
{
	public class ContactCategoriesView
	{
		public Department Department { get; set; }
		public List<ContactCategory> Categories { get; set; }
	}
}
