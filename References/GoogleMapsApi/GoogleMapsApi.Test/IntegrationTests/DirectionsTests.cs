using System;
using System.Collections.Generic;
using System.Linq;
using GoogleMapsApi.Entities.Common;
using GoogleMapsApi.Entities.Directions.Request;
using GoogleMapsApi.Entities.Directions.Response;
using NUnit.Framework;

namespace GoogleMapsApi.Test.IntegrationTests
{
    [TestFixture]
    public class DirectionsTests : BaseTestIntegration
    {

        [Test]
        public void Directions_SumOfStepDistancesCorrect()
        {
            var request = new DirectionsRequest { Origin = "285 Bedford Ave, Brooklyn, NY, USA", Destination = "185 Broadway Ave, Manhattan, NY, USA" };

            var result = GoogleMaps.Directions.Query(request);

            if (result.Status == DirectionsStatusCodes.OVER_QUERY_LIMIT)
                Assert.Inconclusive("Cannot run test since you have exceeded your Google API query limit.");
            Assert.AreEqual(DirectionsStatusCodes.OK, result.Status);
            Assert.Greater(result.Routes.First().Legs.First().Steps.Sum(s => s.Distance.Value), 100);
        }

        [Test]
        public void Directions_WithWayPoints()
        {
            var request = new DirectionsRequest { Origin = "NYC, USA", Destination = "Miami, USA", Waypoints = new string[] { "Philadelphia, USA" }, OptimizeWaypoints = true };

            var result = GoogleMaps.Directions.Query(request);

            if (result.Status == DirectionsStatusCodes.OVER_QUERY_LIMIT)
                Assert.Inconclusive("Cannot run test since you have exceeded your Google API query limit.");
            Assert.AreEqual(DirectionsStatusCodes.OK, result.Status);
            Assert.AreEqual(156097, result.Routes.First().Legs.First().Steps.Sum(s => s.Distance.Value), 10 * 1000);

            StringAssert.Contains("Philadelphia", result.Routes.First().Legs.First().EndAddress);
        }

        [Test]
        public void Directions_Correct_OverviewPath()
        {
            DirectionsRequest request = new DirectionsRequest();
            request.Destination = "maleva 10, Ahtme, Kohtla-Järve, 31025 Ida-Viru County, Estonia";
            request.Origin = "veski 2, Jõhvi Parish, 41532 Ida-Viru County, Estonia";

            DirectionsResponse result = GoogleMaps.Directions.Query(request);

            OverviewPolyline overviewPath = result.Routes.First().OverviewPath;

            OverviewPolyline polyline = result.Routes.First().Legs.First().Steps.First().PolyLine;

            IEnumerable<Location> points = result.Routes.First().OverviewPath.Points;

            Assert.AreEqual(DirectionsStatusCodes.OK, result.Status);
            Assert.AreEqual(122, overviewPath.Points.Count(), 30);
            Assert.AreEqual(2, polyline.Points.Count());
        }

        [Test]
        public void DirectionsAsync_SumOfStepDistancesCorrect()
        {
            var request = new DirectionsRequest { Origin = "285 Bedford Ave, Brooklyn, NY, USA", Destination = "185 Broadway Ave, Manhattan, NY, USA" };

            var result = GoogleMaps.Directions.QueryAsync(request).Result;

            if (result.Status == DirectionsStatusCodes.OVER_QUERY_LIMIT)
                Assert.Inconclusive("Cannot run test since you have exceeded your Google API query limit.");
            Assert.AreEqual(DirectionsStatusCodes.OK, result.Status);
            Assert.Greater(result.Routes.First().Legs.First().Steps.Sum(s => s.Distance.Value), 100);
        }


        //The sub_steps differes between google docs documentation and implementation. We use it as google implemented, so we have test to make sure it's not broken.
        [Test]
        public void Directions_VerifysubSteps()
        {
            var request = new DirectionsRequest
            {
                Origin = "King's Cross station, Euston Road, London",
                Destination = "Lukin St, London",
                TravelMode = TravelMode.Transit,
                DepartureTime = new DateTime(2014, 02, 11, 14, 00, 00)
            };

            DirectionsResponse result = GoogleMaps.Directions.Query(request);

            var substeps = result.Routes.First().Legs.First().Steps.First().SubSteps;

            Assert.NotNull(substeps);
        }
    }
}