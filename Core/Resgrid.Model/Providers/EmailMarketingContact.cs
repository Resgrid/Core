namespace Resgrid.Model.Providers
{
	/// <summary>
	/// A person as the email marketing platform sees them. Only <see cref="UserId"/> or <see cref="EmailAddress"/>
	/// is required; every other value left null is not sent, so the platform keeps what it already holds.
	/// </summary>
	public class EmailMarketingContact
	{
		/// <summary>Resgrid user id. Lets the platform follow the person across an email address change.</summary>
		public string UserId { get; set; }

		public string EmailAddress { get; set; }

		public string FirstName { get; set; }

		public string LastName { get; set; }

		/// <summary>
		/// True subscribes, false unsubscribes, null leaves the platform's current state alone. Null is the only safe value
		/// unless the person themselves made the choice: sending true re-subscribes someone who unsubscribed from an email.
		/// </summary>
		public bool? Subscribed { get; set; }

		/// <summary>Audience segment the contact belongs to, e.g. "Department Admin".</summary>
		public string UserGroup { get; set; }

		/// <summary>Mailing lists to add the contact to.</summary>
		public string[] MailingListIds { get; set; }

		public int? DepartmentId { get; set; }

		public string DepartmentName { get; set; }

		public bool? IsManagingMember { get; set; }
	}
}
