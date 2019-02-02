using System.Runtime.Serialization;
using GoogleMapsApi.Entities.Common;

namespace GoogleMapsApi.Entities.PlaceAutocomplete.Response
{
    /// <summary>
    /// Describes the location of the entered term in the PlaceAutocomplete result text, so that the term can be highlighted if desired
    /// </summary>
    [DataContract]
    public class MatchedSubstring
    {
        /// <summary>
        /// The start position of this term in the description, measured in Unicode characters
        /// </summary>
        [DataMember(Name = "offset")]
        public int Offset { get; set; }

        /// <summary>
        /// The length of this term in the description, measured in Unicode characters
        /// </summary>
        [DataMember(Name = "length")]
        public int Length { get; set; }
    }
}
