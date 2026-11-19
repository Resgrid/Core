namespace Resgrid.Web.Services.Models.v4.Calls
{
	/// <summary>
	/// The result of releasing (standing down) a member from an open call
	/// </summary>
	public class ReleasePersonnelResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public ReleasePersonnelResultData Data { get; set; }

		/// <summary>
		/// Default constructor
		/// </summary>
		public ReleasePersonnelResult()
		{
			Data = new ReleasePersonnelResultData();
		}
	}

	/// <summary>
	/// A member released from a call
	/// </summary>
	public class ReleasePersonnelResultData
	{
		/// <summary>
		/// Id of the call the member was released from
		/// </summary>
		public string CallId { get; set; }

		/// <summary>
		/// User id of the released member
		/// </summary>
		public string UserId { get; set; }

		/// <summary>
		/// When the member was released, ISO 8601 UTC ("Z")
		/// </summary>
		public string ReleasedOn { get; set; }

		/// <summary>
		/// True when the release set the member's status (the department's personnel release status). False when no status
		/// update was confirmed: the member kept their status because they were already available or unavailable, or working
		/// another open call, or the status could not be written (the release itself still stands).
		/// </summary>
		public bool StatusApplied { get; set; }
	}
}
