namespace Resgrid.Config
{
	/// <summary>
	/// Resgrid support helpdesk link for Department Import support requests (wizard plan section 17.5). Resgrid's helpdesk
	/// is Freshdesk; when the domain and API key are blank (self-hosted) requests stay in-app only. Tickets carry metadata
	/// only (conformance I15). Environment keys follow RESGRID__SupportConfig__{Field}.
	/// </summary>
	public static class SupportConfig
	{
		/// <summary>Freshdesk domain, e.g. resgrid.freshdesk.com. Blank = in-app requests only.</summary>
		public static string FreshdeskDomain = "";

		/// <summary>Freshdesk API key (basic auth user; the password is ignored by Freshdesk).</summary>
		public static string FreshdeskApiKey = "";

		/// <summary>Optional Freshdesk group id that receives import tickets.</summary>
		public static string FreshdeskGroupId = "";

		/// <summary>BackOffice base URL used for the deep link in a ticket, e.g. https://backoffice.resgrid.com.</summary>
		public static string BackOfficeBaseUrl = "";
	}
}
