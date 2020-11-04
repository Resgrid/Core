using System.Runtime.Serialization;
using GoogleMapsApi.Entities.Common;

namespace GoogleMapsApi.Entities.PlacesDetails.Response
{
    /// <summary>
    /// Contains the location
    /// </summary>
    [DataContract]
    public class Geometry
    {
        [DataMember(Name = "location")]
        public Location Location { get; set; } 
    }
}