namespace Resgrid.Web.Services.Models.v4.Calls
{
	/// <summary>
	/// One page of a year's archived calls. Page is the zero-based page returned and PageSize the number of calls on it;
	/// continue with Page + 1 while HasMore is true.
	/// </summary>
	public class ArchivedCallsResult : ActiveCallsResult
	{
		/// <summary>True when a later page has more calls.</summary>
		public bool HasMore { get; set; }
	}
}
