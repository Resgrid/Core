namespace Resgrid.Web.Services.Models.v4.Calls
{
	/// <summary>
	/// The result of releasing (standing down) a unit from an open call
	/// </summary>
	public class ReleaseUnitResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public ReleaseUnitResultData Data { get; set; }

		/// <summary>
		/// Default constructor
		/// </summary>
		public ReleaseUnitResult()
		{
			Data = new ReleaseUnitResultData();
		}
	}

	/// <summary>
	/// A unit released from a call
	/// </summary>
	public class ReleaseUnitResultData
	{
		/// <summary>
		/// Id of the call the unit was released from
		/// </summary>
		public string CallId { get; set; }

		/// <summary>
		/// Id of the released unit
		/// </summary>
		public string UnitId { get; set; }

		/// <summary>
		/// When the unit was released, ISO 8601 UTC ("Z")
		/// </summary>
		public string ReleasedOn { get; set; }

		/// <summary>
		/// True when the release set the unit's status (the department's release status); false when the unit kept its status
		/// because it was already available, out of service, or working another open call.
		/// </summary>
		public bool StatusApplied { get; set; }
	}
}
