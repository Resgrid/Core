using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Microsoft.AspNetCore.Html;
using Resgrid.Model;

namespace Resgrid.Web.Helpers
{
	/// <summary>
	/// The icon shown beside a unit's, call's, POI's or role's name in web lists and pickers: the department's uploaded type icon
	/// when there is one, else the type's built-in map icon, else nothing (so rows without a typed icon render as before).
	/// </summary>
	public static class TypeIconHtml
	{
		/// <summary>Root-relative URL of the icon for a unit type, or null when it has none.</summary>
		public static string Url(UnitType type) => TypeIcons.WebIconUrl(type?.IconKey) ?? BuiltInUrl(TypeIcons.BuiltInIconName(type));

		/// <summary>Root-relative URL of the icon for a call type, or null when it has none.</summary>
		public static string Url(CallType type) => TypeIcons.WebIconUrl(type?.IconKey) ?? BuiltInUrl(TypeIcons.BuiltInIconName(type));

		/// <summary>Root-relative URL of a POI type's uploaded icon, or null (POI built-ins are icon-font classes, not images).</summary>
		public static string Url(PoiType type) => TypeIcons.WebIconUrl(type?.IconKey);

		/// <summary>Root-relative URL of a personnel role's uploaded map icon, or null.</summary>
		public static string Url(PersonnelRole role) => TypeIcons.WebIconUrl(role?.IconKey);

		/// <summary>The icon URL for a unit's type name, looked up in the department's types; null when none.</summary>
		public static string UrlForTypeName(IEnumerable<UnitType> types, string typeName)
		{
			if (types == null || string.IsNullOrWhiteSpace(typeName))
				return null;

			return Url(types.FirstOrDefault(x => string.Equals(x.Type, typeName, StringComparison.OrdinalIgnoreCase)));
		}

		/// <summary>The icon URL for a call's type name, looked up in the department's call types; null when none.</summary>
		public static string UrlForCallTypeName(IEnumerable<CallType> types, string typeName)
		{
			if (types == null || string.IsNullOrWhiteSpace(typeName))
				return null;

			return Url(types.FirstOrDefault(x => string.Equals(x.Type, typeName, StringComparison.OrdinalIgnoreCase)));
		}

		/// <summary>A decorative &lt;img&gt; for the icon at <paramref name="size"/> px, or empty content when there is none.</summary>
		public static IHtmlContent Image(string url, int size = 20)
		{
			if (string.IsNullOrWhiteSpace(url))
				return HtmlString.Empty;

			return new HtmlString($"<img src=\"{WebUtility.HtmlEncode(url)}\" alt=\"\" aria-hidden=\"true\" class=\"rg-type-icon\" " +
				$"style=\"width: {size}px; height: {size}px; object-fit: contain; vertical-align: middle; margin-right: 4px;\" />");
		}

		public static IHtmlContent Image(UnitType type, int size = 20) => Image(Url(type), size);

		public static IHtmlContent Image(CallType type, int size = 20) => Image(Url(type), size);

		public static IHtmlContent Image(PoiType type, int size = 20) => Image(Url(type), size);

		public static IHtmlContent Image(PersonnelRole role, int size = 20) => Image(Url(role), size);

		public static IHtmlContent Image(IEnumerable<UnitType> types, string typeName, int size = 20) => Image(UrlForTypeName(types, typeName), size);

		private static string BuiltInUrl(string builtInName) => builtInName == null ? null : $"/images/Mapping/{builtInName}.png";
	}
}
