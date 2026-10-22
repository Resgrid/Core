namespace Resgrid.Web.Services.Models.v4.CallTypes
{
	/// <summary>
	/// A call type
	/// </summary>
	public class CallTypeResultData
	{
		/// <summary>
		/// Id of the call type
		/// </summary>
		public string Id { get; set; }

		/// <summary>
		/// Name of the call type
		/// </summary>
		public string Name { get; set; }

		/// <summary>
		/// What kind of call the type is (CallTypeClasses: 1 Fire, 2 Medical, 3 Rescue, 4 Traffic Collision, 5 Hazardous
		/// Materials, 6 Alarm, 7 Service Call, 8 Law Enforcement, 9 Disaster or Weather, 10 Mutual Aid, 11 Non-Emergency
		/// Activity, 12 Other); -1 when the department has not set one.
		/// </summary>
		public int BaseType { get; set; }

		/// <summary>
		/// Absolute URL of the department's uploaded icon for the type (a 128 px square PNG, anonymous GET, immutable);
		/// null when it has no uploaded icon
		/// </summary>
		public string IconUrl { get; set; }

		/// <summary>Built-in map icon name the type picked (e.g. "fire"); null when none</summary>
		public string MapIcon { get; set; }
	}
}
