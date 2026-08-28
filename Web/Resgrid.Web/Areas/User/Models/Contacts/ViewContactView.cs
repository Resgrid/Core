using System.Collections.Generic;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Contacts
{
	public class ViewContactView
	{
		public string UdfReadOnlyHtml { get; set; }
		public Contact Contact { get; set; }
		public Department Department { get; set; }
		public Address PhysicalAddress { get; set; }
		public Address MailingAddress { get; set; }
		public List<ContactNote> Notes { get; set; }
		public List<ContactNoteType> NoteTypes { get; set; }
		public List<RouteStop> RouteStops { get; set; }
		public List<RoutePlan> RoutePlans { get; set; }

		/// <summary>ADP: true when this contact carries protected fields rendered as REDACTED (plan 7.2).</summary>
		public bool IsProtectedContact { get; set; }
	}
}
