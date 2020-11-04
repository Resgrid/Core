using System.Runtime.Serialization;
using GoogleMapsApi.Entities.Common;

namespace GoogleMapsApi.Entities.Elevation.Response
{
	[DataContract]
	public class Result
	{
		/// <summary>
		/// A location element (containing lat and lng elements) of the position for which elevation data is being computed. Note that for path requests, the set of location elements will contain the sampled points along the path.
		/// </summary>
		[DataMember(Name = "location")]
		public Location Location { get; set; }

		/// <summary>
		/// An elevation element indicating the elevation of the location in meters.
		/// </summary>
		[DataMember(Name = "elevation")]
		public double Elevation { get; set; }
	}
}
