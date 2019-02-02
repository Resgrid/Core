using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using GoogleMapsApi.Entities.Common;
using GoogleMapsApi.Entities.Directions.Request;

namespace GoogleMapsApi.Entities.Directions.Response
{
	[DataContract(Name = "DirectionsResponse")]
	public class DirectionsResponse : IResponseFor<DirectionsRequest>
	{
		[DataMember(Name = "status")]
		public string StatusStr
		{
			get
			{
				return Status.ToString();
			}
			set
			{
				Status = (DirectionsStatusCodes)Enum.Parse(typeof(DirectionsStatusCodes), value);
			}
		}

		/// <summary>
		/// "status" contains metadata on the request. See Status Codes below.
		/// </summary>
		public DirectionsStatusCodes Status { get; set; }

		/// <summary>
		/// "routes" contains an array of routes from the origin to the destination. See Routes below.
		/// </summary>
		[DataMember(Name = "routes")]
		public IEnumerable<Route> Routes { get; set; }


		public override string ToString()
		{
			return string.Format("DirectionsResponse - Status: {0}, Results count: {1}", Status, Routes != null ? Routes.Count() : 0);
		}
	}
}
