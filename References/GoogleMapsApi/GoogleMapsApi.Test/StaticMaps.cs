using System.Collections.Generic;
using GoogleMapsApi.Entities.Common;
using GoogleMapsApi.StaticMaps;
using GoogleMapsApi.StaticMaps.Entities;
using GoogleMapsApi.StaticMaps.Enums;
using NUnit.Framework;

namespace GoogleMapsApi.Test
{
	/// <summary>
	/// Tests examples from - http://code.google.com/apis/maps/documentation/staticmaps/
	/// </summary>
	[TestFixture]
	public class StaticMaps
	{
		[Test(Description = "First basic example")]
		public void BasicTest()
		{
			// downtown New York City
			StaticMapRequest request = new StaticMapRequest(
				new AddressLocation("Brooklyn Bridge,New York,NY"), 14, new ImageSize(512, 512))
				{
					MapType = MapType.Roadmap,
					Markers =
						new List<Marker>
							{
								new Marker
									{
										Style = new MarkerStyle { Color = "blue", Label = "S" },
										Locations = new List<ILocationString> { new Location(40.702147, -74.015794) }
									},
								new Marker
									{
										Style = new MarkerStyle { Color = "green", Label = "G" },
										Locations = new List<ILocationString> { new Location(40.711614, -74.012318) }
									},
								new Marker
									{
										Style = new MarkerStyle { Color = "red", Label = "C" },
										Locations = new List<ILocationString> { new Location(40.718217, -73.998284) }
									}
							},
					Sensor = false
				};
			string expectedResult = "http://maps.google.com/maps/api/staticmap" +
									"?center=Brooklyn%20Bridge%2CNew%20York%2CNY&zoom=14&size=512x512&maptype=roadmap" +
									"&markers=color%3Ablue%7Clabel%3AS%7C40.702147%2C-74.015794&markers=color%3Agreen%7Clabel%3AG%7C40.711614%2C-74.012318" +
									"&markers=color%3Ared%7Clabel%3AC%7C40.718217%2C-73.998284&sensor=false";

			string generateStaticMapURL = new StaticMapsEngine().GenerateStaticMapURL(request);

			Assert.AreEqual(expectedResult, generateStaticMapURL);
		}

		[Test]
		public void AddressTest()
		{
			var request = new StaticMapRequest(new AddressLocation("Berkeley,CA"), 14, new ImageSize(400, 400));
			string expectedResult = "http://maps.google.com/maps/api/staticmap" +
									"?center=Berkeley%2CCA&zoom=14&size=400x400&sensor=false";

			string generateStaticMapURL = new StaticMapsEngine().GenerateStaticMapURL(request);

			Assert.AreEqual(expectedResult, generateStaticMapURL);
		}

		[Test]
		public void ZoomLevels()
		{
			var request = new StaticMapRequest(new Location(40.714728, -73.998672), 12, new ImageSize(400, 400));
			string expectedResult = "http://maps.google.com/maps/api/staticmap" +
									"?center=40.714728%2C-73.998672&zoom=12&size=400x400&sensor=false";

			string generateStaticMapURL = new StaticMapsEngine().GenerateStaticMapURL(request);

			Assert.AreEqual(expectedResult, generateStaticMapURL);
		}

		[Test]
		public void ImageSize()
		{
			var request = new StaticMapRequest(new Location(0, 0), 1, new ImageSize(400, 50));
			string expectedResult = "http://maps.google.com/maps/api/staticmap" +
									"?center=0%2C0&zoom=1&size=400x50&sensor=false";

			string generateStaticMapURL = new StaticMapsEngine().GenerateStaticMapURL(request);

			Assert.AreEqual(expectedResult, generateStaticMapURL);
		}


		[Test]
		public void MapTypes()
		{
			var request = new StaticMapRequest(new Location(40.714728, -73.998672), 12, new ImageSize(400, 400));
			request.MapType = MapType.Terrain;
			string expectedResult = @"http://maps.google.com/maps/api/staticmap" +
									@"?center=40.714728%2C-73.998672&zoom=12&size=400x400&maptype=terrain&sensor=false";

			string actualResult = new StaticMapsEngine().GenerateStaticMapURL(request);

			Assert.AreEqual(expectedResult, actualResult);
		}
	}
}