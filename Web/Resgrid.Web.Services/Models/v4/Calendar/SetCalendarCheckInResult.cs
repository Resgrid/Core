namespace Resgrid.Web.Services.Models.v4.Calendar
{
	/// <summary>
	/// The result of a calendar check-in operation
	/// </summary>
	public class SetCalendarCheckInResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Identifier of the check-in record
		/// </summary>
		public string Id { get; set; }
	}
}
