using GoogleMapsApi.Entities.Common;
using GoogleMapsApi.Entities.TimeZone.Request;
using GoogleMapsApi.Entities.TimeZone.Response;
using NUnit.Framework;

namespace GoogleMapsApi.Test.IntegrationTests
{
    [TestFixture]
    public class TimeZoneTests : BaseTestIntegration
    {
        [Test]
        public void TimeZone_Correct_OverviewPath()
        {
            TimeZoneRequest request = new TimeZoneRequest();
            request.Location = new Location(55.866413, 12.501063);
            request.Language = "en";

            TimeZoneResponse result = GoogleMaps.TimeZone.Query(request);

            Assert.AreEqual(GoogleMapsApi.Entities.TimeZone.Response.Status.OK, result.Status);
        }
    }
}