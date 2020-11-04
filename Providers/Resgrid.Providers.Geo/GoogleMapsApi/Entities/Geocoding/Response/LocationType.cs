using System.Runtime.Serialization;

namespace GoogleMapsApi.Entities.Geocoding.Response
{
	[DataContract]
	public enum GeocodeLocationType
	{
        /// <summary>
        /// Indicates that the returned result is a precise geocode for which we have location information accurate down to street address precision.
        /// </summary>
        [EnumMember]
        ROOFTOP,

        /// <summary>
        /// Indicates that the returned result reflects an approximation (usually on a road) interpolated between two precise points (such as intersections). Interpolated results are generally returned when rooftop geocodes are unavailable for a street address.
        /// </summary>
        [EnumMember]
        RANGE_INTERPOLATED,

        /// <summary>
        /// Indicates that the returned result is the geometric center of a result such as a polyline (for example, a street) or polygon (region).
        /// </summary>
        [EnumMember]
        GEOMETRIC_CENTER,

        /// <summary>
        /// Indicates that the returned result is approximate.
        /// </summary>
        [EnumMember]
        APPROXIMATE
	}
}
