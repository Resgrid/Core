using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using GoogleMapsApi.Entities.PlaceAutocomplete.Request;
using GoogleMapsApi.Entities.PlaceAutocomplete.Response;

namespace GoogleMapsApi.Test.IntegrationTests
{
    [TestFixture]
    class PlaceAutocompleteTests : BaseTestIntegration
    {
        [Test]
        public void ReturnsNoResults()
        {
            var request = new PlaceAutocompleteRequest
            {
                ApiKey = base.ApiKey,
                Input = "zxqtrb",
                Location = new GoogleMapsApi.Entities.Common.Location(53.4635332, -2.2419169),
                Radius = 30000
            };

            PlaceAutocompleteResponse result = GoogleMaps.PlaceAutocomplete.Query(request);

            if (result.Status == Status.OVER_QUERY_LIMIT)
                Assert.Inconclusive("Cannot run test since you have exceeded your Google API query limit.");
            Assert.AreEqual(Status.ZERO_RESULTS, result.Status);
        }

        [Test]
        public void OffsetTest()
        {
            var request = new PlaceAutocompleteRequest
            {
                ApiKey = base.ApiKey,
                Input = "abbeyjibberish",
                Location = new GoogleMapsApi.Entities.Common.Location(53.4635332, -2.2419169),
                Radius = 30000
            };

            PlaceAutocompleteResponse result = GoogleMaps.PlaceAutocomplete.Query(request);

            if (result.Status == Status.OVER_QUERY_LIMIT)
                Assert.Inconclusive("Cannot run test since you have exceeded your Google API query limit.");
            Assert.AreEqual(Status.ZERO_RESULTS, result.Status, "results for jibberish");

            var offsetRequest = new PlaceAutocompleteRequest
            {
                ApiKey = base.ApiKey,
                Input = "abbeyjibberish",
                Offset = 5,
                Location = new GoogleMapsApi.Entities.Common.Location(53.4635332, -2.2419169)
            };

            PlaceAutocompleteResponse offsetResult = GoogleMaps.PlaceAutocomplete.Query(offsetRequest);

            if (offsetResult.Status == Status.OVER_QUERY_LIMIT)
                Assert.Inconclusive("Cannot run test since you have exceeded your Google API query limit.");
            Assert.AreNotEqual(Status.ZERO_RESULTS, offsetResult.Status, "results using offset");
        }

        [Test]
        public void TypeTest()
        {
            var request = new PlaceAutocompleteRequest
            {
                ApiKey = base.ApiKey,
                Input = "abb",
                Types = "geocode",
                Location = new GoogleMapsApi.Entities.Common.Location(53.4635332, -2.2419169),
                Radius = 30000
            };

            PlaceAutocompleteResponse result = GoogleMaps.PlaceAutocomplete.Query(request);

            if (result.Status == Status.OVER_QUERY_LIMIT)
                Assert.Inconclusive("Cannot run test since you have exceeded your Google API query limit.");

            Assert.AreEqual(Status.OK, result.Status);

            foreach (var oneResult in result.Results)
            {
                Assert.IsNotNull(oneResult.Types, "result with no type classification");
                Assert.IsTrue(new List<string>(oneResult.Types).Contains("geocode"), "non-geocode result");
            }
        }


        [TestCase("oakfield road, chea", "CHEADLE")]
        [TestCase("128 abbey r", "MACCLESFIELD")]
        [TestCase("SK10 3PA", "MACCLESFIELD")]
        public void CheckForExpectedRoad( string aSearch, string anExpected)
        {
            var request = new PlaceAutocompleteRequest
            {
                ApiKey = base.ApiKey,
                Input = aSearch,
                Location = new GoogleMapsApi.Entities.Common.Location(53.4635332, -2.2419169),
                Radius = 30000
            };

            PlaceAutocompleteResponse result = GoogleMaps.PlaceAutocomplete.Query(request);

            if (result.Status == Status.OVER_QUERY_LIMIT)
                Assert.Inconclusive("Cannot run test since you have exceeded your Google API query limit.");
            Assert.AreNotEqual(Status.ZERO_RESULTS, result.Status);

            Assert.That(result.Results.Any(t => t.Description.ToUpper().Contains(anExpected)));
        }
    }
}
