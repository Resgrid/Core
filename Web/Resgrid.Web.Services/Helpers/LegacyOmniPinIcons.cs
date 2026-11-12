using System;
using System.Collections.Generic;
using System.Globalization;
using Resgrid.Model;
using Resgrid.Web.Services.Models.v4.Mapping;

namespace Resgrid.Web.Services.Helpers
{
	/// <summary>
	/// TEMP (2026-11-11): Omni store builds released before the pin icon fix draw map markers as a MapLibre symbol layer at
	/// each image's own pixel size, so an uploaded 128 px icon shows four times the size of the 32 px built-in pins.
	/// Until those builds age out, GetMapDataAndMarkers links their markers' IconUrl to the 32 px rendition instead.
	/// <para>
	/// Fixed Omni builds send <c>fullSizeIcons=true</c> and scale the 128 px icon themselves; the legacy store apps (an older
	/// major of the same app, see <see cref="FirstOmniMajor"/>) and the web map draw icons into a 32 px box and keep the
	/// 128 px icon, which stays sharp on high-density screens. Builds that send no app version (web and Electron) are not
	/// recognised and keep the 128 px icon.
	/// </para>
	/// Remove this class, <see cref="TypeIcons.AtMapSize"/>, the TypeIcons endpoint's <c>px</c> parameter and
	/// MapIconPinRenderer.RenderAtMapSize once the Omni fleet is on fixed builds.
	/// </summary>
	public static class LegacyOmniPinIcons
	{
		/// <summary>The app version header every Omni and legacy store build sends with <see cref="ApiClientApplication.Header"/>.</summary>
		public const string AppVersionHeader = "X-Resgrid-App-Version";

		/// <summary>
		/// The first Omni major of each app, by <c>X-Resgrid-Client</c>: Omni took over each legacy app's store listing with a
		/// new major (legacy Responder 10, Unit 7, IC, Dispatch and BigBoard 1).
		/// </summary>
		private static readonly IReadOnlyDictionary<string, int> FirstOmniMajor = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
		{
			["responder"] = 11,
			["unit"] = 8,
			["ic"] = 2,
			["command"] = 2,
			["dispatch"] = 2,
			["bigboard"] = 4
		};

		/// <summary>True for an Omni store build that draws a marker's image at its pixel size and has not said it scales icons.</summary>
		/// <param name="client">The <c>X-Resgrid-Client</c> header.</param>
		/// <param name="appVersion">The <c>X-Resgrid-App-Version</c> header ("8.25").</param>
		/// <param name="fullSizeIcons">The request's <c>fullSizeIcons</c> flag, sent by builds that scale the 128 px icon themselves.</param>
		public static bool Applies(string client, string appVersion, bool fullSizeIcons)
		{
			if (fullSizeIcons || string.IsNullOrWhiteSpace(client) || !FirstOmniMajor.TryGetValue(client.Trim(), out var firstMajor))
				return false;

			return TryGetMajor(appVersion, out var major) && major >= firstMajor;
		}

		/// <summary>Points every marker's uploaded icon at the 32 px rendition.</summary>
		public static void Apply(IEnumerable<MapMakerInfoData> markers)
		{
			if (markers == null)
				return;

			foreach (var marker in markers)
			{
				if (marker != null)
					marker.IconUrl = TypeIcons.AtMapSize(marker.IconUrl);
			}
		}

		private static bool TryGetMajor(string appVersion, out int major)
		{
			major = 0;
			if (string.IsNullOrWhiteSpace(appVersion))
				return false;

			var text = appVersion.Trim();
			var dot = text.IndexOf('.');
			return int.TryParse(dot < 0 ? text : text.Substring(0, dot), NumberStyles.None, CultureInfo.InvariantCulture, out major);
		}
	}
}
