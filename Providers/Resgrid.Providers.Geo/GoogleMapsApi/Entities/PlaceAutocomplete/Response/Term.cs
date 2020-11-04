using System.Runtime.Serialization;
using GoogleMapsApi.Entities.Common;

namespace GoogleMapsApi.Entities.PlaceAutocomplete.Response
{
    /// <summary>
    /// Identifies a section of description in a PlaceAutocomplete search result
    /// </summary>
    [DataContract]
    public class Term
    {
        /// <summary>
        /// The text of the term
        /// </summary>
        [DataMember(Name = "value")]
        public string Value { get; set; }

        /// <summary>
        /// The start position of this term in the description, measured in Unicode characters
        /// </summary>
        [DataMember(Name = "offset")]
        public int Offset { get; set; }
    }
}
