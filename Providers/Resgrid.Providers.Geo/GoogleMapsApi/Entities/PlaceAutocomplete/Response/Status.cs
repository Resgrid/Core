using System.Runtime.Serialization;

namespace GoogleMapsApi.Entities.PlaceAutocomplete.Response
{
	[DataContract]
	public enum Status
	{
		[EnumMember(Value = "OK")]
		OK, // indicates that no errors occurred; the place was successfully detected and at least one result was returned.
		[EnumMember(Value = "ZERO_RESULTS")]
		ZERO_RESULTS, // indicates that the search was successful but returned no results. This may occur if the search was passed a latlng in a remote location.
		[EnumMember(Value = "OVER_QUERY_LIMIT")]
		OVER_QUERY_LIMIT, // indicates that you are over your quota.
		[EnumMember(Value = "REQUEST_DENIED")]
		REQUEST_DENIED, // indicates that your request was denied, generally because of lack of a sensor parameter.
		[EnumMember(Value = "INVALID_REQUEST")]
		INVALID_REQUEST // generally indicates that the input parameter is missing.
	}
}
