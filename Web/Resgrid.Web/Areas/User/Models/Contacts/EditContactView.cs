using System.ComponentModel.DataAnnotations;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Contacts;

public class EditContactView
{
	public string UdfFormHtml { get; set; }
	public string Message { get; set; }

	public Department Department { get; set; }
	public Contact Contact { get; set; }


	public string LocationGpsLatitude { get; set; }
	public string LocationGpsLongitude { get; set; }

	public string EntranceGpsLatitude { get; set; }
	public string EntranceGpsLongitude { get; set; }

	public string ExitGpsLatitude { get; set; }
	public string ExitGpsLongitude { get; set; }

	// Limits mirror the (widened) Addresses columns so client + server validation give feedback before save;
	// every cap is <= its DB column width, so a validated value can never truncate. See M0085.
	[StringLength(500, ErrorMessage = "Street address cannot exceed 500 characters.")]
	public string PhysicalAddress1 { get; set; }

	[StringLength(500, ErrorMessage = "Street address line 2 cannot exceed 500 characters.")]
	public string PhysicalAddress2 { get; set; }

	[StringLength(150, ErrorMessage = "City cannot exceed 150 characters.")]
	public string PhysicalCity { get; set; }

	[StringLength(100, ErrorMessage = "State/Province cannot exceed 100 characters.")]
	public string PhysicalState { get; set; }

	[StringLength(32, ErrorMessage = "Postal code cannot exceed 32 characters.")]
	public string PhysicalPostalCode { get; set; }

	[StringLength(100, ErrorMessage = "Country cannot exceed 100 characters.")]
	public string PhysicalCountry { get; set; }

	public bool MailingAddressSameAsPhysical { get; set; }

	[StringLength(500, ErrorMessage = "Street address cannot exceed 500 characters.")]
	public string MailingAddress1 { get; set; }

	[StringLength(500, ErrorMessage = "Street address line 2 cannot exceed 500 characters.")]
	public string MailingAddress2 { get; set; }

	[StringLength(150, ErrorMessage = "City cannot exceed 150 characters.")]
	public string MailingCity { get; set; }

	[StringLength(100, ErrorMessage = "State/Province cannot exceed 100 characters.")]
	public string MailingState { get; set; }

	[StringLength(32, ErrorMessage = "Postal code cannot exceed 32 characters.")]
	public string MailingPostalCode { get; set; }

	[StringLength(100, ErrorMessage = "Country cannot exceed 100 characters.")]
	public string MailingCountry { get; set; }
}
