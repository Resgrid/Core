namespace Resgrid.Web.Services.Models.v4.Inbox
{
	/// <summary>
	/// The Novu subscriberHash for a unit's inbox
	/// </summary>
	public class UnitSubscriberHashResult : StandardApiResponseV4Base
	{
		/// <summary>
		/// Response Data
		/// </summary>
		public UnitSubscriberHashResultData Data { get; set; }

		/// <summary>
		/// Default constructor
		/// </summary>
		public UnitSubscriberHashResult()
		{
			Data = new UnitSubscriberHashResultData();
		}
	}

	/// <summary>
	/// A unit's Novu inbox credentials
	/// </summary>
	public class UnitSubscriberHashResultData
	{
		/// <summary>
		/// Unit id
		/// </summary>
		public string UnitId { get; set; }

		/// <summary>
		/// Novu subscriberHash for the unit's inbox subscriber ({DepartmentCode}_Unit_{unitId}). Pass it with the
		/// subscriber id when opening the inbox; required once the Novu environment enforces HMAC. Null when Novu
		/// is not configured.
		/// </summary>
		public string SubscriberHash { get; set; }
	}
}
