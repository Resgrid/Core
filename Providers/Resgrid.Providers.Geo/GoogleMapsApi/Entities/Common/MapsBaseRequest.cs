using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Net;
using System.Linq;

namespace GoogleMapsApi.Entities.Common
{
    public abstract class MapsBaseRequest
    {
        public MapsBaseRequest()
        {
            this.isSsl = true;
            Sensor = false;
            ApiKey = null;
        }


        /// <summary>
        /// True to indicate that request comes from a device with a location sensor, otherwise false. 
        /// This information is required by Google and does not affect the results.
        /// </summary>
        /// <remarks>
        /// It is unclear if Google refers to the device performing the request or the source of the location data.
        /// In the geocoding API reference at https://developers.google.com/maps/documentation/geocoding/ the definition is:
        /// 
        ///     sensor (required) — Indicates whether or not the geocoding request comes from a device with a location sensor.
        /// 
        /// This implies that only mobile devices that are equipped with a location sensor (such as GPS) should set the Sensor value 
        /// to True. So if a location is sent to a web server which then calls the Google API, it apparently should set the Sensor
        /// value to false, since the web server isn't a location sensor equipped device.
        /// 
        /// In another page of their documentation, https://developers.google.com/maps/documentation/javascript/tutorial they say:
        /// 
        ///     The sensor parameter of the URL must be included, and indicates whether this application uses a sensor (such as a GPS 
        ///     locator) to determine the user's location.
        /// 
        /// This implies something completely different, that the Sensor value must be set to true if the source of the location
        /// information is a sensor or not, regardless if the request is being performed by a location sensor equipped device or not.
        /// </remarks>
        public bool Sensor { get; set; }

        /// <summary>
        /// True to use use the https protocol; false to use http. The default is false.
        /// </summary>
        public virtual bool IsSSL
        {
            get { return isSsl; }
            set { isSsl = value; }
        }
        private bool isSsl;


        protected internal virtual string BaseUrl
        {
            get
            {
                return "maps.googleapis.com/maps/api/";
            }
        }

        /// <summary>
        /// Your application's API key. This key identifies your application for purposes of quota management and so that Places 
        /// added from your application are made immediately available to your app. Visit the APIs Console to create an API Project and obtain your key.
        /// </summary>
        public string ApiKey { get; set; }

        protected virtual QueryStringParametersList GetQueryStringParameters()
        {
            QueryStringParametersList parametersList = new QueryStringParametersList();

            parametersList.Add("sensor", Sensor.ToString().ToLower());

            if (!string.IsNullOrWhiteSpace(ApiKey))
            {
                if (!this.IsSSL)
                {
                    throw new ArgumentException("When using an ApiKey MUST send the request over SSL [IsSSL = true]");
                }
                parametersList.Add("key", ApiKey);
            }

            return parametersList;
        }

        public virtual Uri GetUri()
        {
            string scheme = IsSSL ? "https://" : "http://";

            var queryString = GetQueryStringParameters().GetQueryStringPostfix();
            return new Uri(scheme + BaseUrl + "json?" + queryString);
        }
    }
}
