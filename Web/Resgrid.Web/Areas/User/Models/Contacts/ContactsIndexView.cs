using Resgrid.Model;
using System.Collections.Generic;

namespace Resgrid.Web.Areas.User.Models.Contacts
{
	public class ContactsIndexView
	{
		public Department Department { get; set; }
		public List<Contact> Contacts { get; set; }
		public List<ContactCategory> ContactCategories { get; set; }
		public string TreeData { get; set; }
	}
}
