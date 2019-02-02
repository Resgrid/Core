using System;
using System.Runtime.Serialization;

namespace GoogleMapsApi.Entities.PlacesDetails.Response
{
    [DataContract]
    public class Event
    {


        /// <summary>
        /// Event id.
        /// </summary>
        [DataMember(Name = "event_id")]
        public string EventId { get; set; }

        public DateTime StartTime;

        [DataMember(Name = "start_time")]
        internal int int_StartTime
        {
            get
            {
                return GoogleMapsApi.Engine.UnixTimeConverter.DateTimeToUnixTimestamp(StartTime);
            }
            set
            {
                DateTime epoch = new DateTime(1970, 1, 1);
                StartTime = epoch.AddSeconds(value);
            }
        }

        [DataMember(Name = "summary")]
        public string Summary { get; set; }

        [DataMember(Name = "url")]
        public string Url { get; set; }

    }
}
