using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Resgrid.Model;
using Resgrid.Model.Providers;
using SharpKml.Dom;
using SharpKml.Engine;

namespace Resgrid.Providers.GeoLocationProvider
{
	public class KmlProvider: IKmlProvider
	{
		public List<Coordinates> ImportFile(Stream input, bool isKmz)
		{
			var coordinates = new List<Coordinates>();

			try
			{
				KmlFile file;
				if (isKmz)
				{
					var kmz = KmzFile.Open(input);
					file = kmz.GetDefaultKmlFile();
				}
				else
					file = KmlFile.Load(input);


				Kml kml = file.Root as Kml;
				if (kml != null)
				{
					foreach (var placemark in kml.Flatten().OfType<Placemark>())
					{
						Console.WriteLine(placemark.Name);

						var coords = new Coordinates();
						coords.Latitude = placemark.CalculateBounds().Center.Latitude;
						coords.Longitude = placemark.CalculateBounds().Center.Longitude;

						coordinates.Add(coords);
					}
				}
			}
			catch
			{
				
			}
			
			return coordinates;
		}
	}
}