using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Contacts
{
	public class ViewContactView
	{
		public Contact Contact { get; set; }
		public Department Department { get; set; }
		public Address PhysicalAddress { get; set; }
		public Address MailingAddress { get; set; }
		public List<ContactNote> Notes { get; set; }
		public List<ContactNoteType> NoteTypes { get; set; }
	}
}
