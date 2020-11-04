using System;
using System.Runtime.Serialization;
using GoogleMapsApi.Entities.Common;

namespace GoogleMapsApi.Entities.PlaceAutocomplete.Response
{
    /// <summary>
    /// When the PlaceAutocomplete service returns results from a search, it places them within a predictions array. 
    /// Even if the service returns no results (such as if the location is remote) it still returns an empty predictions array.
    /// </summary>
    [DataContract]
	public class Prediction
	{
        /// <summary>
        /// Contains the human-readable name for the returned result. For establishment results, this is usually the business name.
        /// </summary>
		[DataMember(Name = "description")]
		public string Description { get; set; }

		/// <summary>
		/// Contains a unique identifier for this place. To retrieve information about this place, pass this identifier in the placeId field of
		/// a Places API request.
		/// </summary>
		[DataMember( Name = "place_id" )]
		public string PlaceId { get; set; }
		
		/// <summary>
        /// Contains a unique token that you can use to retrieve additional information about this place in a Place Details request. 
        /// You can store this token and use it at any time in future to refresh cached data about this place, but the same token is 
        /// not guaranteed to be returned for any given place across different searches.
        /// </summary>
        [DataMember(Name = "reference")]
		[Obsolete( "Use place_id instead.  See https://developers.google.com/places/documentation/search#deprecation for more information." )]
		public string Reference { get; set; }

        /// <summary>
        /// Contains a unique stable identifier denoting this place. This identifier may not be used to retrieve information about this
        /// place, but can be used to consolidate data about this place, and to verify the identity of a place across separate searches.
        /// </summary>
        [DataMember(Name = "id")]
		[Obsolete( "Use place_id instead.  See https://developers.google.com/places/documentation/search#deprecation for more information." )]
		public string ID { get; set; }

        /// <summary>
        /// Contains an array of terms identifying each section of the returned description (a section of the description is generally 
        /// terminated with a comma). Each entry in the array has a value field, containing the text of the term, and an offset field, 
        /// defining the start position of this term in the description, measured in Unicode characters.
        /// </summary>
		[DataMember(Name = "terms")]
		public Term[] Terms { get; set; }

        /// <summary>
        /// Contains an array of types that apply to this place. For example: [ "political", "locality" ] or [ "establishment", "geocode" ].
        /// </summary>
		[DataMember(Name = "types")]
		public string[] Types { get; set; }

        /// <summary>
        /// Contains an offset value and a length. These describe the location of the entered term in the prediction result text, so that 
        /// the term can be highlighted if desired.
        /// </summary>
		[DataMember(Name = "matched_substrings")]
		public MatchedSubstring[] MatchedSubstrings { get; set; }
	}
}
