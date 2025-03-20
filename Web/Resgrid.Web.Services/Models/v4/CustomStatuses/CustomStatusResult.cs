namespace Resgrid.Web.Services.Models.v4.CustomStatuses
{
	/// <summary>
	/// Depicts a custom status in the Resgrid system.
	/// </summary>
	public class CustomStatusResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public CustomStatusResultData Data { get; set; }
	}

	/// <summary>
	/// Custom Status
	/// </summary>
	public class CustomStatusResultData
	{
		/// <summary>
		/// Custom Status Id
		/// </summary>
		public string Id { get; set; }

		/// <summary>
		/// Custom Status Type
		/// </summary>
		public int Type { get; set; }

		/// <summary>
		/// State Id
		/// </summary>
		public string StateId { get; set; }

		/// <summary>
		/// Text for the Custom Status
		/// </summary>
		public string Text { get; set; }

		/// <summary>
		/// Button Color
		/// </summary>
		public string BColor { get; set; }

		/// <summary>
		/// Text Color
		/// </summary>
		public string Color { get; set; }

		/// <summary>
		/// Require GPS for this Status
		/// </summary>
		public bool Gps { get; set; }

		/// <summary>
		/// Is the Note Required or Optional
		/// </summary>
		public int Note { get; set; }

		/// <summary>
		/// Detail type id
		/// </summary>
		public int Detail { get; set; }

		/// <summary>
		/// Is this custom status deleted (only should be used for display)
		/// </summary>
		public bool IsDeleted { get; set; }
	}
}
