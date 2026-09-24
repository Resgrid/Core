using System.Linq;
using Newtonsoft.Json.Linq;

namespace Resgrid.Web.Mcp.Tools
{
	/// <summary>
	/// Helpers for reshaping v4 API responses. The v4 API serializes PascalCase property names and wraps
	/// every payload in a "Data" property on the standard response envelope.
	/// </summary>
	internal static class V4ResponseReader
	{
		/// <summary>MapMakerInfoData.Type of a unit marker in Mapping/GetMapDataAndMarkers.</summary>
		public const int UnitMarkerType = 1;

		/// <summary>MapMakerInfoData.Type of a personnel marker in Mapping/GetMapDataAndMarkers.</summary>
		public const int PersonnelMarkerType = 3;

		/// <summary>
		/// The status and staffing fields of a Personnel/GetAllPersonnelInfos record, without its contact details.
		/// </summary>
		public static readonly string[] PersonnelStatusFields =
		{
			"UserId", "FirstName", "LastName", "GroupName", "StatusId", "Status", "StatusTimestamp",
			"StatusDestinationName", "StaffingId", "Staffing", "StaffingTimestamp"
		};

		public static JArray GetDataArray(JObject response) => response?["Data"] as JArray ?? new JArray();

		/// <summary>
		/// Copies only the named fields of each item, so a tool returns what it describes rather than the whole record.
		/// </summary>
		public static JArray Project(JArray items, params string[] fields)
		{
			var projected = new JArray();

			foreach (var item in items.OfType<JObject>())
			{
				var copy = new JObject();
				foreach (var field in fields)
					copy[field] = item[field];

				projected.Add(copy);
			}

			return projected;
		}

		/// <summary>
		/// Extracts one kind of marker from a Mapping/GetMapDataAndMarkers response. The API has already applied the
		/// department's location TTLs and the caller's location-view permissions, and leaves out anything without a
		/// current position. Marker ids carry a one-letter type prefix ("u" + unit id, "p" + user id) that is removed here.
		/// </summary>
		public static JArray GetMapMarkers(JObject response, int markerType, string idField)
		{
			var markers = new JArray();

			if (response?["Data"]?["MapMakerInfos"] is not JArray infos)
				return markers;

			foreach (var info in infos.OfType<JObject>().Where(x => x.Value<int?>("Type") == markerType))
			{
				var id = info.Value<string>("Id");

				markers.Add(new JObject
				{
					[idField] = string.IsNullOrEmpty(id) ? id : id.Substring(1),
					["Name"] = info["Title"],
					["Latitude"] = info["Latitude"],
					["Longitude"] = info["Longitude"]
				});
			}

			return markers;
		}
	}
}
