using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Helpers;

namespace Resgrid.Tests.Web.User
{
	/// <summary>
	/// The Indoor Location search on the call forms (IndoorMaps/SearchZones): only rooms marked dispatchable come back (a
	/// corridor is not a call location), each with its centre so picking one can move the call's map pin.
	/// </summary>
	[TestFixture]
	public class IndoorZoneSearchTests
	{
		private const int DepartmentId = 31;

		[TearDown]
		public void TearDown() => ClaimsAuthorizationHelper._httpContextAccessor = null;

		private static IndoorMapsController Controller(Mock<IIndoorMapService> service)
		{
			var http = new DefaultHttpContext
			{
				User = new ClaimsPrincipal(new ClaimsIdentity(new[]
				{
					new Claim(ClaimTypes.PrimarySid, "dispatcher-1"),
					new Claim(ClaimTypes.PrimaryGroupSid, DepartmentId.ToString())
				}, "test"))
			};
			ClaimsAuthorizationHelper._httpContextAccessor = new HttpContextAccessor { HttpContext = http };
			return new IndoorMapsController(service.Object, NullLogger<IndoorMapsController>.Instance)
			{
				ControllerContext = new ControllerContext { HttpContext = http }
			};
		}

		[Test]
		public async Task SearchZones_returns_dispatchable_rooms_with_their_centre()
		{
			var service = new Mock<IIndoorMapService>();
			service.Setup(x => x.SearchZonesAsync(DepartmentId, "zone")).ReturnsAsync(new List<IndoorMapZone>
			{
				new IndoorMapZone { IndoorMapZoneId = "z14", Name = "Formation Zone 14", IndoorMapFloorId = "l1", IsDispatchable = true, CenterLatitude = 32.8425m, CenterLongitude = -111.9012m },
				new IndoorMapZone { IndoorMapZoneId = "aisle", Name = "Main aisle (zone side)", IndoorMapFloorId = "l1", IsDispatchable = false }
			});

			var result = await Controller(service).SearchZones("zone");

			var json = JObject.FromObject(((JsonResult)result).Value);
			var results = json["results"].ToList();
			results.Should().ContainSingle();
			results[0]["id"].Value<string>().Should().Be("z14");
			results[0]["text"].Value<string>().Should().Be("Formation Zone 14");
			results[0]["floorId"].Value<string>().Should().Be("l1");
			results[0]["lat"].Value<decimal>().Should().Be(32.8425m);
			results[0]["lng"].Value<decimal>().Should().Be(-111.9012m);
		}

		[Test]
		public async Task SearchZones_with_no_term_returns_nothing_without_searching()
		{
			var service = new Mock<IIndoorMapService>();

			var result = await Controller(service).SearchZones(" ");

			JObject.FromObject(((JsonResult)result).Value)["results"].Should().BeEmpty();
			service.Verify(x => x.SearchZonesAsync(It.IsAny<int>(), It.IsAny<string>()), Times.Never);
		}
	}
}
