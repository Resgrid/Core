using System;
using System.Runtime.Serialization;

namespace GoogleMapsApi.Entities.PlacesDetails.Response
{
    [DataContract]
    public class Review
    {
        /// <summary>
        /// Event id.
        /// </summary>
        [DataMember(Name = "aspect")]
        public Aspect Aspect { get; set; }

        [DataMember(Name = "author_name")]
        public string AuthorName { get; set; }

        [DataMember(Name = "author_url")]
        public string AuthorUrl { get; set; }

        public DateTime Time;

        [DataMember(Name = "time")]
        internal int int_StartTime
        {
            get
            {
                return GoogleMapsApi.Engine.UnixTimeConverter.DateTimeToUnixTimestamp(Time);
            }
            set
            {
                DateTime epoch = new DateTime(1970, 1, 1);
                Time = epoch.AddSeconds(value);
            }
        }

        [DataMember(Name = "text")]
        public string Text { get; set; }

    }
}
