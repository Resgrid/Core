using System;
using System.Runtime.Serialization;

namespace GoogleMapsApi.Entities.PlacesDetails.Response
{
    [DataContract]
    public class TimeOfWeek
    {
        /// <remarks>
        /// .net's DayOfWeek follows the same numbering as google (Sunday == 0) therefor a specific
        /// conversion step is not needed, the deserializer's direct casting will work.
        /// http://msdn.microsoft.com/en-us/library/system.dayofweek.aspx
        /// </remarks>
        [DataMember(Name = "day")]
        public DayOfWeek Day { get; set; }

        /// <summary>
        /// May contain a time of day in 24-hour hhmm format (values are in the range 0000–2359). The time will
        /// be reported in the Place’s timezone.
        /// </summary>
        /// <remarks>
        /// Didn't make this stronly typed due to .net's lack of an explicit 'Time' class.
        /// http://noda-time.blogspot.com.au/2011/08/what-wrong-with-datetime-anyway.html
        /// </remarks>
        [DataMember(Name = "time")]
        public int Time { get; set; }
    }
}
