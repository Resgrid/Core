using System.Linq;
using GoogleMapsApi.Entities.Common;
using GoogleMapsApi.Entities.Elevation.Request;
using NUnit.Framework;

namespace GoogleMapsApi.Test.IntegrationTests
{
    [TestFixture]
    public class ElevationTests : BaseTestIntegration
    {
        [Test]
        public void Elevation_ReturnsCorrectElevation()
        {
            var request = new ElevationRequest { Locations = new[] { new Location(40.7141289, -73.9614074) } };

            var result = GoogleMaps.Elevation.Query(request);

            if (result.Status == Entities.Elevation.Response.Status.OVER_QUERY_LIMIT)
                Assert.Inconclusive("Cannot run test since you have exceeded your Google API query limit.");
            Assert.AreEqual(Entities.Elevation.Response.Status.OK, result.Status);
            Assert.AreEqual(14.782454490661619, result.Results.First().Elevation);
        }

        [Test]
        public void ElevationAsync_ReturnsCorrectElevation()
        {
            var request = new ElevationRequest { Locations = new[] { new Location(40.7141289, -73.9614074) } };

            var result = GoogleMaps.Elevation.QueryAsync(request).Result;

            if (result.Status == Entities.Elevation.Response.Status.OVER_QUERY_LIMIT)
                Assert.Inconclusive("Cannot run test since you have exceeded your Google API query limit.");
            Assert.AreEqual(Entities.Elevation.Response.Status.OK, result.Status);
            Assert.AreEqual(14.782454490661619, result.Results.First().Elevation);
        } 
    }
}