using System.Runtime.Serialization;

namespace GoogleMapsApi.Entities.Directions.Response
{
	[DataContract]
	public enum DirectionsStatusCodes
	{
		[EnumMember]
		OK, // indicates the response contains a valid result.
		[EnumMember]
		NOT_FOUND, // indicates at least one of the locations specified in the requests's origin, destination, or waypoints could not be geocoded.
		[EnumMember]
		ZERO_RESULTS, // indicates no route could be found between the origin and destination.
		[EnumMember]
		MAX_WAYPOINTS_EXCEEDED, // indicates that too many waypointss were provided in the request The maximum allowed waypoints is 8, plus the origin, and destination. ( Google Maps Premier customers may contain requests with up to 23 waypoints.)
		[EnumMember]
		INVALID_REQUEST, // indicates that the provided request was invalid.
		[EnumMember]
		OVER_QUERY_LIMIT, // indicates the service has received too many requests from your application within the allowed time period.
		[EnumMember]
		REQUEST_DENIED, // indicates that the service denied use of the directions service by your application.
		[EnumMember]
		UNKNOWN_ERROR, // indicates a directions request could not be processed due to a server error. The request may succeed if you try again
	}
}
