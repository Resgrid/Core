using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using GoogleMapsApi.Entities.Common;
using GoogleMapsApi.Entities.PlaceAutocomplete.Request;

namespace GoogleMapsApi.Entities.PlaceAutocomplete.Response
{
	[DataContract]
    public class PlaceAutocompleteResponse : IResponseFor<PlaceAutocompleteRequest>
	{
		/// <summary>
		/// "status" contains metadata on the request.
		/// </summary>
		public Status Status { get; set; }

		[DataMember(Name = "status")]
		internal string StatusStr
		{
			get
			{
				return Status.ToString();
			}
			set
			{
				Status = (Status)Enum.Parse(typeof(Status), value);
			}
		}

		/// <summary>
		/// "results" contains an array of predictions rather than full results, each including a description and a reference which can be queried further
        /// to get the full place details
		/// </summary>
		[DataMember(Name = "predictions")]
		public IEnumerable<Prediction> Results { get; set; }
	}
}
