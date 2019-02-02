using System;
using System.Linq;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using GoogleMapsApi.Entities.Common;
using GoogleMapsApi.Entities.Geocoding.Request;
using NUnit.Framework;
using Status = GoogleMapsApi.Entities.Geocoding.Response.Status;

namespace GoogleMapsApi.Test.IntegrationTests
{
    [TestFixture]
    public class GeocodingTests : BaseTestIntegration
    {
        [Test]
        public void Geocoding_ReturnsCorrectLocation()
        {
            var request = new GeocodingRequest
            {
                ApiKey = ApiKey,
                Address = "285 Bedford Ave, Brooklyn, NY 11211, USA"
            };

            var result = GoogleMaps.Geocode.Query(request);

            if (result.Status == Status.OVER_QUERY_LIMIT)
                Assert.Inconclusive("Cannot run test since you have exceeded your Google API query limit.");
            Assert.AreEqual(Status.OK, result.Status);
            Assert.AreEqual("40.7140415,-73.9613119", result.Results.First().Geometry.Location.LocationString);
        }


        [Test]
        public void GeocodingAsync_ReturnsCorrectLocation()
        {
            var request = new GeocodingRequest { Address = "285 Bedford Ave, Brooklyn, NY 11211, USA" };

            var result = GoogleMaps.Geocode.QueryAsync(request).Result;

            if (result.Status == Status.OVER_QUERY_LIMIT)
                Assert.Inconclusive("Cannot run test since you have exceeded your Google API query limit.");
            Assert.AreEqual(Status.OK, result.Status);
            Assert.AreEqual("40.7140415,-73.9613119", result.Results.First().Geometry.Location.LocationString);
        }

        [Test]
        [ExpectedException(typeof(AuthenticationException))]
        public void Geocoding_InvalidClientCredentials_Throws()
        {
            var request = new GeocodingRequest { Address = "285 Bedford Ave, Brooklyn, NY 11211, USA", ClientID = "gme-ThisIsAUnitTest", SigningKey = "AAECAwQFBgcICQoLDA0ODxAREhM=" };

            GoogleMaps.Geocode.Query(request);
        }

        [Test]
        public void GeocodingAsync_InvalidClientCredentials_Throws()
        {
            var request = new GeocodingRequest { Address = "285 Bedford Ave, Brooklyn, NY 11211, USA", ClientID = "gme-ThisIsAUnitTest", SigningKey = "AAECAwQFBgcICQoLDA0ODxAREhM=" };

            Assert.Throws(Is.TypeOf<AggregateException>().And.InnerException.TypeOf<AuthenticationException>(),
                          () => GoogleMaps.Geocode.QueryAsync(request).Wait());
        }

        [Test]
        [ExpectedException(typeof(TimeoutException))]
        public void Geocoding_TimeoutTooShort_Throws()
        {
            var request = new GeocodingRequest { Address = "285 Bedford Ave, Brooklyn, NY 11211, USA" };

            GoogleMaps.Geocode.Query(request, TimeSpan.FromMilliseconds(1));
        }

        [Test]
        public void GeocodingAsync_TimeoutTooShort_Throws()
        {
            var request = new GeocodingRequest { Address = "285 Bedford Ave, Brooklyn, NY 11211, USA" };

            Assert.Throws(Is.TypeOf<AggregateException>().And.InnerException.TypeOf<TimeoutException>(),
                () => GoogleMaps.Geocode.QueryAsync(request, TimeSpan.FromMilliseconds(1)).Wait());
        }

        [Test]
        public void GeocodingAsync_Cancel_Throws()
        {
            var request = new GeocodingRequest { Address = "285 Bedford Ave, Brooklyn, NY 11211, USA" };

            var tokeSource = new CancellationTokenSource();
            var task = GoogleMaps.Geocode.QueryAsync(request, tokeSource.Token);
            tokeSource.Cancel();

            Assert.Throws(Is.TypeOf<AggregateException>().And.InnerException.TypeOf<TaskCanceledException>(),
                () => task.Wait());
        }

        [Test]
        public void GeocodingAsync_WithPreCanceledToken_Cancels()
        {
            var request = new GeocodingRequest { Address = "285 Bedford Ave, Brooklyn, NY 11211, USA" };
            var cts = new CancellationTokenSource();
            cts.Cancel();

            var task = GoogleMaps.Geocode.QueryAsync(request, cts.Token);

            Assert.Throws(Is.TypeOf<AggregateException>().And.InnerException.TypeOf<TaskCanceledException>(),
                            () => task.Wait());
        }

        [Test]
        public void ReverseGeocoding_ReturnsCorrectAddress()
        {
            var request = new GeocodingRequest { Location = new Location(40.7141289, -73.9614074) };

            var result = GoogleMaps.Geocode.Query(request);

            if (result.Status == Status.OVER_QUERY_LIMIT)
                Assert.Inconclusive("Cannot run test since you have exceeded your Google API query limit.");
            Assert.AreEqual(Status.OK, result.Status);
            Assert.AreEqual("281 Bedford Avenue, Brooklyn, NY 11211, USA", result.Results.First().FormattedAddress);
        }

        [Test]
        public void ReverseGeocodingAsync_ReturnsCorrectAddress()
        {
            var request = new GeocodingRequest { Location = new Location(40.7141289, -73.9614074) };

            var result = GoogleMaps.Geocode.QueryAsync(request).Result;

            if (result.Status == Status.OVER_QUERY_LIMIT)
                Assert.Inconclusive("Cannot run test since you have exceeded your Google API query limit.");
            Assert.AreEqual(Status.OK, result.Status);
            Assert.AreEqual("281 Bedford Avenue, Brooklyn, NY 11211, USA", result.Results.First().FormattedAddress);
        }



    }
}