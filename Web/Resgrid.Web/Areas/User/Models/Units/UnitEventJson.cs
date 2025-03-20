namespace Resgrid.Web.Areas.User.Models.Units
{
    public class UnitEventJson
    {
		public int EventId { get; set; }
        public int UnitId { get; set; }
        public string UnitName { get; set; }
        public string State { get; set; }
        public string Timestamp { get; set; }
        public string LocalTimestamp { get; set; }
        public string DestinationName { get; set; }
        public string Latitude { get; set; }
        public string Longitude { get; set; }
        public string Accuracy { get; set; }
        public string Altitude { get; set; }
        public string AltitudeAccuracy { get; set; }
        public string Speed { get; set; }
        public string Heading { get; set; }
        public string Note { get; set; }

    }
}