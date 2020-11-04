using System.Runtime.Serialization;

namespace GoogleMapsApi.Entities.PlacesDetails.Response
{
    /// <summary>
    /// periods[] is an array of opening periods covering seven days, starting from Sunday, in chronological order. Each period contains:
    /// open contains a pair of day and time objects describing when the Place opens:
    /// day a number from 0–6, corresponding to the days of the week, starting on Sunday. For example, 2 means Tuesday.
    /// time may contain a time of day in 24-hour hhmm format. Values are in the range 0000–2359. The time will be reported in the Place’s time zone.
    /// close may contain a pair of day and time objects describing when the Place closes. Note: If a Place is always open, the close section will be missing from the response. Clients can rely on always-open being represented as an open period containing day with value 0 and time with value 0000, and no close.
    /// </summary>
    [DataContract]
    public class Period
    {
        [DataMember(Name = "open")]
        public TimeOfWeek OpenTime { get; set; }

        [DataMember(Name = "close")]
        public TimeOfWeek CloseTime { get; set; }
    }
}
