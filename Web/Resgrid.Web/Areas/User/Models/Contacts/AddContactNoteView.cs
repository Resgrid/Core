using System;
using System.ComponentModel.DataAnnotations;

namespace Resgrid.Web.Areas.User.Models.Contacts
{
	public class AddContactNoteView
	{
		[Required]
		public string Note { get; set; }

		[Required]
		public string ContactId { get; set; }
		public string ContactNoteTypeId { get; set; }
		public DateTime? ExpiresOn  { get; set; }
		public bool ShouldAlert { get; set; }
	}
}
