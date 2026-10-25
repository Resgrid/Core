using System.Collections.Generic;
using System.Linq;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Mapping
{
	/// <summary>One icon as the web map draws it, from the site's own origin.</summary>
	public class MapLegendIcon
	{
		public string Url { get; set; }

		/// <summary>A department upload (a square drawn at 32 x 32); otherwise a built-in 32 x 37 pin.</summary>
		public bool IsUploaded { get; set; }

		/// <summary>An upload made for this very status or priority.</summary>
		public bool IsVariant { get; set; }

		/// <summary>The status or priority style, or null when it has no meaning.</summary>
		public string Style { get; set; }

		public static MapLegendIcon From(MapMarkerIcon icon)
		{
			if (icon == null)
				return null;

			if (icon.IconKey != null)
				return new MapLegendIcon { Url = TypeIcons.WebIconUrl(icon.IconKey, icon.BadgeStyle), IsUploaded = true, IsVariant = icon.IconIsVariant, Style = icon.Style };

			return new MapLegendIcon
			{
				Url = icon.PinName != null ? TypeIcons.WebPinUrl(icon.PinName) : $"/images/Mapping/{icon.ImagePath}.png",
				Style = icon.Style
			};
		}
	}

	public class MapLegendPoiType
	{
		public string Name { get; set; }
		public string Color { get; set; }
		public string IconClass { get; set; }
		public string IconUrl { get; set; }
	}

	public class MapIconLegendView
	{
		public MapIconLegend Legend { get; set; } = new MapIconLegend();
		public List<MapLegendPoiType> PoiTypes { get; set; } = new List<MapLegendPoiType>();
		public bool CanEditTypes { get; set; }
		public bool CanEditRoles { get; set; }
	}

	/// <summary>The status or priority icons page of one call type, unit type or role.</summary>
	public class StatusIconsView
	{
		public int Kind { get; set; }
		public int TypeId { get; set; }
		public string TypeName { get; set; }

		/// <summary>The type's own icon (uploaded or built-in), as it shows with no status or priority.</summary>
		public MapLegendIcon OwnIcon { get; set; }

		public List<StatusIconRow> Rows { get; set; } = new List<StatusIconRow>();
		public string Message { get; set; }
		public string ErrorMessage { get; set; }
	}

	public class StatusIconRow
	{
		public MapIconLegendState State { get; set; }

		/// <summary>What shows when no icon is uploaded for this status or priority.</summary>
		public MapLegendIcon DefaultIcon { get; set; }

		/// <summary>The icon uploaded for it, or null.</summary>
		public string UploadedUrl { get; set; }

		/// <summary>
		/// The icon page rows: each state, what shows without an upload for it (the type's own icon with the state's badge,
		/// else the built-in pin in the state's style) and the upload if any.
		/// </summary>
		public static List<StatusIconRow> ForRow(MapIconResolver resolver, TypeIconKinds kind, int typeId, MapIconLegendRow row)
		{
			var ownIcon = row.OwnIcon;

			return row.States.Select(state =>
			{
				var uploadedKey = resolver.VariantIconKey(kind, typeId, state.Variant);
				var withoutUpload = new MapMarkerIcon
				{
					ImagePath = ownIcon.ImagePath,
					PinBase = ownIcon.PinBase,
					Style = state.Style,
					Variant = state.Variant,
					IconKey = ownIcon.IconIsVariant ? null : ownIcon.IconKey
				};

				return new StatusIconRow
				{
					State = state,
					DefaultIcon = MapLegendIcon.From(withoutUpload),
					UploadedUrl = uploadedKey != null ? TypeIcons.WebIconUrl(uploadedKey) : null
				};
			}).ToList();
		}
	}
}
