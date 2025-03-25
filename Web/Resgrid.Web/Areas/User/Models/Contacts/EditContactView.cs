using System.ComponentModel.DataAnnotations;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Contacts;

public class EditContactView
{
	public string Message { get; set; }

	public Department Department { get; set; }
	public Contact Contact { get; set; }


	public string LocationGpsLatitude { get; set; }
	public string LocationGpsLongitude { get; set; }

	public string EntranceGpsLatitude { get; set; }
	public string EntranceGpsLongitude { get; set; }

	public string ExitGpsLatitude { get; set; }
	public string ExitGpsLongitude { get; set; }

	[MaxLength(100)]
	public string PhysicalAddress1 { get; set; }

	[MaxLength(100)]
	public string PhysicalAddress2 { get; set; }

	[MaxLength(100)]
	public string PhysicalCity { get; set; }

	[MaxLength(50)]
	public string PhysicalState { get; set; }

	[MaxLength(50)]
	public string PhysicalPostalCode { get; set; }

	[MaxLength(100)]
	public string PhysicalCountry { get; set; }

	public bool MailingAddressSameAsPhysical { get; set; }

	[MaxLength(100)]
	public string MailingAddress1 { get; set; }

	[MaxLength(100)]
	public string MailingAddress2 { get; set; }

	[MaxLength(100)]
	public string MailingCity { get; set; }

	[MaxLength(50)]
	public string MailingState { get; set; }

	[MaxLength(50)]
	public string MailingPostalCode { get; set; }

	[MaxLength(100)]
	public string MailingCountry { get; set; }
}
