using System.Runtime.Serialization;

namespace GoogleMapsApi.Entities.PlacesDetails.Response
{
    [DataContract]
    public enum Status
    {
        /// <summary>
        /// Indicates that no errors occurred; the place was successfully detected and at least one result was returned.
        /// </summary>
        [EnumMember(Value = "OK")]
        OK,

        /// <summary>
        /// Indicates a server-side error; trying again may be successful.
        /// </summary>
        [EnumMember(Value = "UNKNOWN_ERROR")]
        UNKNOWN_ERROR,

        /// <summary>
        /// Indicates that the search was successful but returned no results. This may occur if the search was passed a latlng in a remote location.
        /// </summary>
        [EnumMember(Value = "ZERO_RESULTS")]
        ZERO_RESULTS,

        /// <summary>
        /// Indicates that you are over your quota.
        /// </summary>
        [EnumMember(Value = "OVER_QUERY_LIMIT")]
        OVER_QUERY_LIMIT,

        /// <summary>
        /// Indicates that your request was denied, generally because of lack of a sensor parameter.
        /// </summary>
        [EnumMember(Value = "REQUEST_DENIED")]
        REQUEST_DENIED,

        /// <summary>
        /// Generally indicates that the query parameter (location or radius) is missing.
        /// </summary>
        [EnumMember(Value = "INVALID_REQUEST")]
        INVALID_REQUEST,

        /// <summary>
        /// Indicates that the referenced location was not found in the Places database.
        /// </summary>
        [EnumMember(Value = "NOT_FOUND")]
        NOT_FOUND,
    }
}
