using System;
using System.Globalization;
using System.Runtime.Serialization;
using System.Collections.Generic;

namespace GoogleMapsApi.Entities.PlacesDetails.Response
{
    [DataContract]
    public class Result
    {
        /// <summary>
        /// name contains the human-readable name for the returned result. For establishment results, this is usually the canonicalized business name.
        /// </summary>
    
        [DataMember(Name="address_components")]
        public IEnumerable<GoogleMapsApi.Entities.Geocoding.Response.AddressComponent> AddressComponent { get; set; }


        [DataMember(Name = "events")]
        public IEnumerable<Event> Event { get; set; }

        [DataMember(Name = "formatted_address")]
        public string FormattedAddress { get; set; }

        [DataMember(Name = "formatted_phone_number")]
        public string FormattedPhoneNumber { get; set; }

        [DataMember(Name = "geometry")]
        public Geometry Geometry { get; set; }

        [DataMember(Name = "icon")]
        public string Icon { get; set; }

        [DataMember(Name = "id")]
        public string ID { get; set; }

        [DataMember(Name = "international_phone_number")]
        public string InternationalPhoneNumber { get; set; }

        [DataMember(Name = "name")]
        public string Name { get; set; }

        /// <summary>
        /// Opening hours information
        /// </summary>
        [DataMember(Name = "opening_hours")]
        public OpeningHours OpeningHours { get; set; }

        public PriceLevel? PriceLevel;

        [DataMember(Name = "price_level")]
        internal string string_PriceLevel
        {
            get { return PriceLevel.HasValue ? ((int) PriceLevel).ToString(CultureInfo.InvariantCulture) : null; }
            set
            {
                if (value == null)
                    PriceLevel = null;
                else
                {
                    int priceLevelInt;
                    if (int.TryParse(value, out priceLevelInt))
                        PriceLevel = (PriceLevel) priceLevelInt;
                    else
                        PriceLevel = null;
                }
            }
        }

        [DataMember(Name = "rating")]
        public double Rating { get; set; }

        [DataMember(Name = "reference")]
        [Obsolete("Use place_id instead.  See https://developers.google.com/places/documentation/search#deprecation for more information.")]
        public string Reference { get; set; }

        [DataMember(Name = "reviews")]
        public IEnumerable<Review> Review { get; set; }

        [DataMember(Name = "types")]
        public string[] Types { get; set; }

        [DataMember(Name = "url")]
        public string URL { get; set; }

        [DataMember(Name = "utc_offset")]
        public string UTCOffset { get; set; }

        [DataMember(Name = "vicinity")]
        public string Vicinity { get; set; }

        [DataMember(Name = "website")]
        public string Website { get; set; }

        [DataMember(Name = "place_id")]
        public string PlaceId { get; set; }
    }

    public enum PriceLevel
    {
        Free = 0,
        Inexpensive = 1,
        Moderate = 2,
        Expensive = 3,
        VeryExpensive = 4,
    }
}
