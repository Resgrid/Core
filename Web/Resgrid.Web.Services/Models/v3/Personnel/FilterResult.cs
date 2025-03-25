namespace Resgrid.Web.Services.Controllers.Version3.Models.Personnel
{
	/// <summary>
	/// The result of getting all personnel filters for the system
	/// </summary>
	public class FilterResult
	{
		/// <summary>
		/// The Id value of the filter
		/// </summary>
		public string Id { get; set; }

		/// <summary>
		/// The type of the filter
		/// </summary>
		public string Type { get; set; }

		/// <summary>
		/// The filters name
		/// </summary>
		public string Name { get; set; }
	}
}
