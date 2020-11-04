using System.Runtime.Serialization;
using GoogleMapsApi.Entities.Common;

namespace GoogleMapsApi.Entities.Places.Response
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
