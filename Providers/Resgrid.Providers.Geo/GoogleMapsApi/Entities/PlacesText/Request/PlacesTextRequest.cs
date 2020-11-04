using System;
using System.Globalization;
using GoogleMapsApi.Entities.Common;

namespace GoogleMapsApi.Entities.PlacesText.Request
{
    public class PlacesTextRequest : MapsBaseRequest
    {
        protected internal override string BaseUrl
        {
            get { return "maps.googleapis.com/maps/api/place/textsearch/"; }
        }

        public string Query { get; set; } // required

        public Location Location { get; set; } // optional
        public double? Radius { get; set; } // optional
        public string Language { get; set; } // optional
        public string Types { get; set; } // optional

        public override bool IsSSL
        {
            get { return true; }
            set { throw new NotSupportedException("This operation is not supported, PlacesRequest must use SSL"); }
        }

        protected override QueryStringParametersList GetQueryStringParameters()
        {
            if (string.IsNullOrWhiteSpace(Query))
                throw new ArgumentException("Query must be provided.");

            if (string.IsNullOrWhiteSpace(ApiKey))
                throw new ArgumentException("ApiKey must be provided");

            QueryStringParametersList parameters = base.GetQueryStringParameters();

            parameters.Add("query", Query);
            

            if (Location != null) parameters.Add("location", Location.ToString());
            if (Radius != null) parameters.Add("radius", Radius.Value.ToString(CultureInfo.InvariantCulture));
            if (!string.IsNullOrWhiteSpace(Language)) parameters.Add("language", Language);
            if (!string.IsNullOrWhiteSpace(Types)) parameters.Add("types", Types);

            return parameters;
        }
    }
}
