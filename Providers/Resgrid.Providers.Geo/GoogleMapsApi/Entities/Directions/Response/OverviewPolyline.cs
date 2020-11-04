using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using GoogleMapsApi.Entities.Common;

namespace GoogleMapsApi.Entities.Directions.Response
{
	/// <summary>
	/// Contains the encoded and decoded data returned in the overview_polyline field.
	/// </summary>
	[DataContract]
	public class OverviewPolyline
	{
		/// <summary>
		/// The encoded string containing the overview path points as they were received.
		/// </summary>
		[DataMember(Name = "points")]
		internal string EncodedPoints { get; set; }

		private Lazy<IEnumerable<Location>> pointsLazy;

		/// <summary>
		/// An array of Location objects representing the points in the overview path, decoded from the string contained in the EncodedPoints property.
		/// </summary>
		/// <exception cref="PointsDecodingException">Unexpectedly couldn't decode points</exception>
		public IEnumerable<Location> Points { get { return pointsLazy.Value; } }

		public OverviewPolyline()
		{
			InitLazyPoints(default(StreamingContext));
		}

		//NOTE that the CTOR isn't called when Deserialized so we use the Attribute
		[OnDeserializing]
		private void InitLazyPoints(StreamingContext contex)
		{
			pointsLazy = new Lazy<IEnumerable<Location>>(DecodePoints);
		}

		// Adapted from http://jeffreysambells.com/2010/05/27/decoding-polylines-from-google-maps-direction-api-with-java
		// The algorithm explained here - https://developers.google.com/maps/documentation/utilities/polylinealgorithm
		/// <summary>
		/// 
		/// </summary>
		/// <returns></returns>
		/// <exception cref="PointsDecodingException">Unexpectedly couldn't decode points</exception>
		private IEnumerable<Location> DecodePoints()
		{
			IEnumerable<Location> points;

			try
			{
				var poly = new List<Location>();
				int index = 0;
				int lat = 0;
				int lng = 0;

				while (index < EncodedPoints.Length)
				{
					int b, shift = 0, result = 0;
					do
					{
						b = EncodedPoints[index++] - 63;
						result |= (b & 0x1f) << shift;
						shift += 5;
					} while (b >= 0x20);
					int dlat = ((result & 1) != 0 ? ~(result >> 1) : (result >> 1));
					lat += dlat;

					shift = 0;
					result = 0;
					do
					{
						b = EncodedPoints[index++] - 63;
						result |= (b & 0x1f) << shift;
						shift += 5;
					} while (b >= 0x20);
					int dlng = ((result & 1) != 0 ? ~(result >> 1) : (result >> 1));
					lng += dlng;

					poly.Add(new Location(lat / 1E5, lng / 1E5));
				}

				points = poly.ToArray();
			}
			catch (Exception ex)
			{
				throw new PointsDecodingException("Couldn't decode points", EncodedPoints, ex);
			}

			return points;
		}

		/// <summary>
		/// The RAW data of points from Google
		/// </summary>
		/// <returns></returns>
		public string GetRawPointsData()
		{
			return this.EncodedPoints;
		}
	}
}