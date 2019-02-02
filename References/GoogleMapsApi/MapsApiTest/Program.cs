using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using GoogleMapsApi;
using GoogleMapsApi.Entities.Common;
using GoogleMapsApi.Entities.Directions.Request;
using GoogleMapsApi.Entities.Directions.Response;
using GoogleMapsApi.Entities.Elevation.Request;
using GoogleMapsApi.Entities.Geocoding.Request;
using GoogleMapsApi.Entities.Geocoding.Response;
using GoogleMapsApi.StaticMaps;
using GoogleMapsApi.StaticMaps.Entities;

namespace MapsApiTest
{
	class Program
	{
		static void Main(string[] args)
		{
			// Driving directions
			var drivingDirectionRequest = new DirectionsRequest
			{
				Origin = "NYC, 5th and 39",
				Destination = "Philladephia, Chesnut and Wallnut"
			};

			DirectionsResponse drivingDirections = GoogleMaps.Directions.Query(drivingDirectionRequest);
			PrintDirections(drivingDirections);

			// Transit directions
			var transitDirectionRequest = new DirectionsRequest
			{
				Origin = "New York",
				Destination = "Queens",
				TravelMode = TravelMode.Transit,
				DepartureTime = DateTime.Now
			};

			DirectionsResponse transitDirections = GoogleMaps.Directions.Query(transitDirectionRequest);
			PrintDirections(transitDirections);

			// Geocode
			var geocodeRequest = new GeocodingRequest
			{
				Address = "new york city",
			};

			GeocodingResponse geocode = GoogleMaps.Geocode.Query(geocodeRequest);
			Console.WriteLine(geocode);

			// Static maps API - get static map of with the path of the directions request
			var staticMapGenerator = new StaticMapsEngine();

			//Path from previous directions request
			IEnumerable<Step> steps = drivingDirections.Routes.First().Legs.First().Steps;
			// All start locations
			IList<ILocationString> path = steps.Select(step => step.StartLocation).ToList<ILocationString>();
			// also the end location of the last step
			path.Add(steps.Last().EndLocation);

			string url = staticMapGenerator.GenerateStaticMapURL(new StaticMapRequest(new Location(40.38742, -74.55366), 9, new ImageSize(800, 400))
			{
				Pathes = new List<Path> { new Path
					{
						Style = new PathStyle
						{
							Color = "red"
						},
						Locations = path
					}}
			});

			Console.WriteLine("Map with path: " + url);

			// Async! (Elevation)
			var elevationRequest = new ElevationRequest
			{
				Locations = new[] { new Location(54, 78) },
			};

			var task = GoogleMaps.Elevation.QueryAsync(elevationRequest)
				.ContinueWith(t => Console.WriteLine("\n" + t.Result));

			Console.Write("Asynchronous query sent, waiting for a reply..");

			while (!task.IsCompleted)
			{
				Console.Write('.');
				Thread.Sleep(1000);
			}

			Console.WriteLine("Finished! Press any key to exit...");
			Console.ReadKey();
		}

		private static void PrintDirections(DirectionsResponse directions)
		{
			Route route = directions.Routes.First();
			Leg leg = route.Legs.First();

			foreach (Step step in leg.Steps)
			{
				Console.WriteLine(StripHTML(step.HtmlInstructions));
			}

			Console.WriteLine();
		}

		private static string StripHTML(string html)
		{
			return Regex.Replace(html, @"<(.|\n)*?>", string.Empty);
		}
	}
}