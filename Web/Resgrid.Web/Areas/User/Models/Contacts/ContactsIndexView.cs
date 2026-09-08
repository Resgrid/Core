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

		/// <summary>Contacts whose pre-plan review date has passed (Contacts plan Phase A, A6 index badge).</summary>
		public HashSet<string> PreplanReviewOverdueContactIds { get; set; } = new HashSet<string>();
	}
}
