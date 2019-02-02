using System.Configuration;
using System.Linq;
using GoogleMapsApi.Entities.PlacesText.Request;
using GoogleMapsApi.Entities.PlacesText.Response;
using NUnit.Framework;

namespace GoogleMapsApi.Test.IntegrationTests
{
    [TestFixture]
    public class PlacesTextTests : BaseTestIntegration
    {
        [Test]
        public void ReturnsFormattedAddress()
        {
            var request = new PlacesTextRequest
                              {
                                  ApiKey = ApiKey,
                                  Query = "1 smith st parramatta",
                                  Types = "street_address"
                              };

            PlacesTextResponse result = GoogleMaps.PlacesText.Query(request);

            if (result.Status == Status.OVER_QUERY_LIMIT)
                Assert.Inconclusive("Cannot run test since you have exceeded your Google API query limit.");
            Assert.AreEqual(Status.OK, result.Status);
            Assert.AreEqual("1 Smith St, Parramatta NSW 2150, Australia", result.Results.First().FormattedAddress);
        }
    }
}