using System;
using System.Runtime.Serialization;

namespace GoogleMapsApi.Entities.PlacesDetails.Response
{
    [DataContract]
    public class Aspect
    {
        /// <summary>
        /// Event id.
        /// </summary>
        [DataMember(Name = "rating")]
        public int Rating { get; set; }

        [DataMember(Name = "type")]
        public string Type { get; set; }

    }
}
