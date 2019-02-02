using System.Collections.Generic;
using System.Runtime.Serialization;
using GoogleMapsApi.Entities.Common;

namespace GoogleMapsApi.Entities.Directions.Response
{
	/// <summary>
	/// Each element in the legs array specifies a single leg of the journey from the origin to the destination in the calculated route. For routes that contain no waypoints, the route will consist of a single "leg," but for routes that define one or more waypoints, the route will consist of one or more legs, corresponding to the specific legs of the journey.
	/// </summary>
	[DataContract(Name = "leg")]
	public class Leg
	{
		/// <summary>
		/// steps[] contains an array of steps denoting information about each separate step of the leg of the journey. (See Directions Steps below.)
		/// </summary>
		[DataMember(Name = "steps")]
		public IEnumerable<Step> Steps { get; set; }

		/// <summary>
		/// distance indicates the total distance covered by this leg
		/// </summary>
		[DataMember(Name = "distance")]
		public Distance Distance { get; set; }

		/// <summary>
		/// duration indicates the total duration of this leg,
		/// </summary>
		[DataMember(Name = "duration")]
		public Duration Duration { get; set; }

		/// <summary>
		/// start_location contains the latitude/longitude coordinates of the origin of this leg. Because the Directions API calculates directions between locations by using the nearest transportation option (usually a road) at the start and end points, start_location may be different than the provided origin of this leg if, for example, a road is not near the origin.
		/// </summary>
		[DataMember(Name = "start_location")]
		public Location StartLocation { get; set; }

		/// <summary>
		/// end_location contains the latitude/longitude coordinates of the given destination of this leg. Because the Directions API calculates directions between locations by using the nearest transportation option (usually a road) at the start and end points, end_location may be different than the provided destination of this leg if, for example, a road is not near the destination.
		/// </summary>
		[DataMember(Name = "end_location")]
		public Location EndLocation { get; set; }

		/// <summary>
		/// start_address contains the human-readable address (typically a street address) reflecting the start_location of this leg.
		/// </summary>
		[DataMember(Name = "start_address")]
		public string StartAddress { get; set; }

		/// <summary>
		/// end_addresss contains the human-readable address (typically a street address) reflecting the end_location of this leg.
		/// </summary>
		[DataMember(Name = "end_address")]
		public string EndAddress { get; set; }

		/// <summary>
		/// The time of arrival. Only avaliable when using TravelMode = Transit
		/// </summary>
		[DataMember(Name = "arrival_time")]
		public Duration ArrivalTime { get; set; }

		/// <summary>
		/// The time of departure. Only avaliable when using TravelMode = Transit
		/// </summary>
		[DataMember(Name = "departure_time")]
		public Duration DepartureTime { get; set; }
	}
}
