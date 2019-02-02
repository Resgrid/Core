using System;
using GoogleMapsApi.Engine;
using GoogleMapsApi.Entities.Common;

namespace GoogleMapsApi.Entities.TimeZone.Request
{
    public class TimeZoneRequest : SignableRequest
    {
        protected internal override string BaseUrl
        {
            get { return base.BaseUrl + "timezone/"; }
        }

        /// <summary>
        /// location: a comma-separated lat,lng tuple (eg. location=-33.86,151.20), representing the location to look up
        /// </summary>
        public Location Location { get; set; } // required
        
        /// <summary>
        /// Timestamp specifies the desired time as seconds since midnight, January 1, 1970 UTC. The Time Zone API uses the timestamp to determine whether or not Daylight Savings should be applied. Times before 1970 can be expressed as negative values.
        /// </summary>
        public DateTime TimeStamp { get; set; } // required 
        
        /// <summary>
        /// The language in which to return results. See the list of supported domain languages. Note that we often update supported languages so this list may not be exhaustive. Defaults to en
        /// </summary>
        public string Language { get; set; } // optional

        /// <summary>
        /// The language in which to return results. See the list of supported domain languages. Note that we often update supported languages so this list may not be exhaustive. Defaults to en.
        /// </summary>
        public override bool IsSSL
        {
            get { return true; }
            set { throw new NotSupportedException("This operation is not supported, TimeZoneRequest must use SSL"); }
        }

        protected override QueryStringParametersList GetQueryStringParameters()
        {
            if (Location == null)
                throw new ArgumentException("Location is required");

            if (TimeStamp == null)
                throw new ArgumentException("TimeStamp is required");

            var parameters = base.GetQueryStringParameters();

            parameters.Add("location", this.Location.LocationString);
            parameters.Add("timestamp", UnixTimeConverter.DateTimeToUnixTimestamp(this.TimeStamp).ToString());

            if (!string.IsNullOrWhiteSpace(Language)) parameters.Add("language", Language);

            return parameters;
        }
    }
}
