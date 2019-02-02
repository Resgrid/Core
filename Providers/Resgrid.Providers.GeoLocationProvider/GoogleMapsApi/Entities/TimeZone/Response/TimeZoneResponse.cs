using System;
using System.Runtime.Serialization;
using GoogleMapsApi.Entities.Common;
using GoogleMapsApi.Entities.TimeZone.Request;

namespace GoogleMapsApi.Entities.TimeZone.Response
{
    [DataContract]
    public class TimeZoneResponse : IResponseFor<TimeZoneRequest>
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
        /// DstOffset: the offset for daylight-savings time in seconds. This will be zero if the time zone is not in Daylight Savings Time during the specified timestamp.
        /// </summary>
        [DataMember(Name = "dstOffset")]
        public double OffSet { get; set; }

        /// <summary>
        /// RawOffset: the offset from UTC (in seconds) for the given location. This does not take into effect daylight savings.
        /// </summary>
        [DataMember(Name = "rawOffset")]
        public double RawOffSet { get; set; }
        
        /// <summary>
        /// TimeZoneId: a string containing the ID of the time zone, such as "America/Los_Angeles" or "Australia/Sydney".
        /// </summary>
        [DataMember(Name = "timeZoneId")]
        public string TimeZoneId { get; set; }
        
        /// <summary>
        /// TimeZoneName: a string containing the long form name of the time zone. This field will be localized if the language parameter is set. eg. "Pacific Daylight Time" or "Australian.
        /// </summary>
        [DataMember(Name = "timeZoneName")]
        public string TimeZoneName { get; set; }
    }
}
 
 

