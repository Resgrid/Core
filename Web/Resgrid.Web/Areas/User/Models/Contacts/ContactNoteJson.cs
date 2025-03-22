using System;

namespace Resgrid.WebCore.Areas.User.Models.Contacts
{
	public class ContactNoteJson
	{
		public string ContactNoteId { get; set; }

		public string ContactNoteTypeId { get; set; }

		public string ContactNoteType { get; set; }

		public string Note { get; set; }

		public bool ShouldAlert { get; set; }

		public string ExpiresOn { get; set; }
		public string CreatedOn { get; set; }
		public string CreatedBy { get; set; }

		public string BackgroundColor { get; set; }
	}
}
