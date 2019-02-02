using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Text;
using System.Web;
using GoogleMapsApi.Entities.Common;
using GoogleMapsApi.StaticMaps.Entities;
using GoogleMapsApi.StaticMaps.Enums;

namespace GoogleMapsApi.StaticMaps
{
	/// <summary>
	/// Creates a URL to google's static map according to propery filled up StaticMapsRequest
	/// http://code.google.com/apis/maps/documentation/staticmaps/
	/// </summary>
	public class StaticMapsEngine
	{
		protected static readonly string BaseUrl;

		static StaticMapsEngine()
		{
			BaseUrl = @"maps.google.com/maps/api/staticmap";
		}

		public string GenerateStaticMapURL(StaticMapRequest request)
		{
			string scheme = request.IsSSL ? "https://" : "http://";

			var parametersList = new QueryStringParametersList();
			
			if (request.Center != null)
			{
				ILocationString center = request.Center;

				string centerLocation = center.LocationString;

				parametersList.Add("center", centerLocation);
			}

			if (request.Zoom != default(int))
			{
				parametersList.Add("zoom", request.Zoom.ToString());
			}

			if (request.Size.Width != default(int) || request.Size.Height != default(int))
			{
				ImageSize imageSize = request.Size;

				parametersList.Add("size", string.Format("{0}x{1}", imageSize.Width, imageSize.Height));
			}
			else
			{
				throw new ArgumentException("Size is invalid");
			}

			if (request.ImageFormat != default(ImageFormat))
			{
				string format;

				switch (request.ImageFormat)
				{
					case ImageFormat.PNG8:
						format = "png8";
						break;
					case ImageFormat.PNG32:
						format = "png32";
						break;
					case ImageFormat.GIF:
						format = "gif";
						break;
					case ImageFormat.JPG:
						format = "jpg";
						break;
					case ImageFormat.JPG_baseline:
						format = "jpg-baseline";
						break;
					default:
						throw new ArgumentOutOfRangeException("ImageFormat");
				}

				parametersList.Add("format", format);
			}

			if (request.MapType != null)
			{
				string type;

				switch (request.MapType)
				{
					case MapType.Roadmap:
						type = "roadmap";
						break;
					case MapType.Satellite:
						type = "satellite";
						break;
					case MapType.Terrain:
						type = "terrain";
						break;
					case MapType.Hybrid:
						type = "hybrid";
						break;
					default:
						throw new ArgumentOutOfRangeException("MapType");
				}

				parametersList.Add("maptype", type);
			}

			if (request.Style != null)
			{
				MapStyle style = request.Style;

				var styleComponents = new List<string>();

				if (style.MapFeature != default(MapFeature))
				{
					string mapFeature;

					switch (style.MapFeature)
					{
						case MapFeature.All:
							mapFeature = "all";
							break;
						case MapFeature.Road:
							mapFeature = "road";
							break;
						case MapFeature.Landscape:
							mapFeature = "landscape";
							break;
						default:
							throw new ArgumentOutOfRangeException();
					}

					styleComponents.Add("feature:" + mapFeature);
				}

				if (style.MapElement != default(MapElement))
				{
					string element;

					switch (style.MapElement)
					{
						case MapElement.All:
							element = "all";
							break;
						case MapElement.Geometry:
							element = "geometry";
							break;
						case MapElement.Labels:
							element = "lables";
							break;
						default:
							throw new ArgumentOutOfRangeException();
					}

					styleComponents.Add("element:" + element);
				}

				string hue = style.HUE;
				if (hue != null)
				{
					styleComponents.Add("hue:" + hue);
				}

				float? lightness = style.Lightness;
				if (lightness != null)
				{
					styleComponents.Add("lightness:" + lightness);
				}


				float? saturation = style.Saturation;
				if (saturation != null)
				{
					styleComponents.Add("saturation:" + saturation);
				}

				float? gamma = style.Gamma;
				if (gamma != null)
				{
					styleComponents.Add("hue:" + gamma);
				}

				bool inverseLightness = style.InverseLightness;
				if (inverseLightness)
				{
					styleComponents.Add("inverse_lightnes:true");
				}

				MapVisibility mapVisibility = style.MapVisibility;

				if (mapVisibility != default(MapVisibility))
				{
					string visibility;

					switch (mapVisibility)
					{
						case MapVisibility.On:
							visibility = "on";
							break;
						case MapVisibility.Off:
							visibility = "off";
							break;
						case MapVisibility.Simplified:
							visibility = "simplified";
							break;
						default:
							throw new ArgumentOutOfRangeException();
					}

					styleComponents.Add("visibility:" + visibility);
				}

				parametersList.Add("style", string.Join("|", styleComponents));
			}

			IList<Marker> markers = request.Markers;

			if (markers != null)
			{
				foreach (Marker marker in markers)
				{
					var markerStyleParams = new List<string>();

					MarkerStyle markerStyle = marker.Style;
					if (markerStyle != null)
					{
						if (string.IsNullOrWhiteSpace(markerStyle.Color))
						{
							throw new ArgumentException("Marker style color can't be empty");
						}

						markerStyleParams.Add("color:" + markerStyle.Color);

						if (!string.IsNullOrWhiteSpace(markerStyle.Label))
						{
							markerStyleParams.Add("label:" + markerStyle.Label);
						}

						if (markerStyle.Size != default(MarkerSize))
						{
							switch (markerStyle.Size)
							{
								case MarkerSize.Mid:
									markerStyleParams.Add("size:mid");
									break;
								case MarkerSize.Tiny:
									markerStyleParams.Add("size:tiny");
									break;
								case MarkerSize.Small:
									markerStyleParams.Add("size:small");
									break;
								default:
									throw new ArgumentOutOfRangeException();
							}
						}
					}

					string styleString = string.Join("|", markerStyleParams);

					string locations = string.Join("|", marker.Locations.Select(location => location.LocationString));

					parametersList.Add("markers", string.Format("{0}|{1}", styleString, locations));
				}
			}

			IList<Path> pathes = request.Pathes;

			if (pathes != null)
			{
				foreach (Path path in pathes)
				{
					var pathStyleParams = new List<string>();

					PathStyle pathStyle = path.Style;

					if (pathStyle != null)
					{
						if (string.IsNullOrWhiteSpace(pathStyle.Color))
						{
							throw new ArgumentException("Path style color can't be empty");
						}

						pathStyleParams.Add("color:" + pathStyle.Color);

						if (!string.IsNullOrWhiteSpace(pathStyle.FillColor))
						{
							pathStyleParams.Add("fillcolor:" + pathStyle.FillColor);
						}

						if (pathStyle.Weight != default(int))
						{
							pathStyleParams.Add("weight:" + pathStyle.Weight);
						}
					}

					string styleString = string.Join("|", pathStyleParams);

					string locations = string.Join("|", path.Locations.Select(location => location.LocationString));

					parametersList.Add("path", string.Format("{0}|{1}", styleString, locations));
				}
			}

			parametersList.Add("sensor", request.Sensor ? "true" : "false");

			return scheme + BaseUrl + "?" + parametersList.GetQueryStringPostfix();
		}
	}
}