using System.Collections.Generic;
using System.Runtime.Serialization;

namespace GoogleMapsApi.Entities.Geocoding.Response
{
	/// <summary>
	/// When the geocoder returns results, it places them within a (JSON) results array. Even if the geocoder returns no results (such as if the address doesn't exist) it still returns an empty results array. (XML responses consist of zero or more result elements.)
	/// </summary>
	[DataContract]
	public class Result
	{
		/// <summary>
		/// The types[] array indicates the type of the returned result. This array contains a set of one or more tags identifying the type of feature returned in the result. For example, a geocode of "Chicago" returns "locality" which indicates that "Chicago" is a city, and also returns "political" which indicates it is a political entity.
		/// </summary>
		[DataMember(Name = "types")]
		public IEnumerable<string> Types { get; set; }

		/// <summary>
		/// formatted_address is a string containing the human-readable address of this location. Often this address is equivalent to the "postal address," which sometimes differs from country to country. (Note that some countries, such as the United Kingdom, do not allow distribution of true postal addresses due to licensing restrictions.) This address is generally composed of one or more address components. For example, the address "111 8th Avenue, New York, NY" contains separate address components for "111" (the street number, "8th Avenue" (the route), "New York" (the city) and "NY" (the US state). These address components contain additional information as noted below.
		/// </summary>
		[DataMember(Name = "formatted_address")]
		public string FormattedAddress { get; set; }

		/// <summary>
		/// address_components[] is an array containing the separate address components
		/// </summary>
		[DataMember(Name = "address_components")]
		public IEnumerable<AddressComponent> AddressComponents { get; set; }

		/// <summary>
		/// geometry contains information such as latitude, longitude, location_type, viewport, bounds
		/// </summary>
		[DataMember(Name = "geometry")]
		public Geometry Geometry { get; set; }

        /// <summary>
        /// indicates that the geocoder did not return an exact match for the original request, though it did match part of the requested address. You may wish to examine the original request for misspellings and/or an incomplete address. Partial matches most often occur for street addresses that do not exist within the locality you pass in the request.
        /// </summary>
        [DataMember(Name = "partial_match")]
        public bool PartialMatch { get; set; }
	}
}
