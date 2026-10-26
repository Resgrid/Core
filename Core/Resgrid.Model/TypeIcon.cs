using System;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.RegularExpressions;
using ProtoBuf;

namespace Resgrid.Model
{
	/// <summary>Which kind of department type a <see cref="TypeIcon"/> belongs to. Persisted; append only.</summary>
	public enum TypeIconKinds
	{
		UnitType = 1,
		CallType = 2,
		PoiType = 3,
		/// <summary>A personnel role (M0286); a person's map pin uses the icon of their first role, in role order, that has one.</summary>
		PersonnelRole = 4
	}

	/// <summary>Why an uploaded file cannot be a type icon; the web pages show a localized message per reason.</summary>
	public enum TypeIconRejections
	{
		None = 0,
		/// <summary>No file, or an empty one.</summary>
		Required = 1,
		/// <summary>Larger than the byte limit.</summary>
		TooManyBytes = 2,
		/// <summary>Not decodable as an image.</summary>
		Unreadable = 3,
		/// <summary>An image, but not PNG, JPEG, GIF or WebP (SVG, BMP, TIFF and the rest).</summary>
		UnsupportedFormat = 4,
		/// <summary>Wider or taller than the edge limit.</summary>
		TooLarge = 5,
		/// <summary>Smaller than the minimum on its long edge.</summary>
		TooSmall = 6
	}

	/// <summary>
	/// The key, kind and slot of one uploaded status or priority icon, without its bytes: what the map needs to pick an
	/// icon. Cached per department.
	/// </summary>
	[ProtoContract]
	public class TypeIconVariant
	{
		[ProtoMember(1)]
		public int TypeKind { get; set; }

		[ProtoMember(2)]
		public int TypeId { get; set; }

		[ProtoMember(3)]
		public string Variant { get; set; }

		[ProtoMember(4)]
		public string IconKey { get; set; }
	}

	/// <summary>
	/// A department's uploaded icon for one unit, call or POI type (migration M0285) or personnel role (M0286): the server
	/// re-encoded, square, transparent PNG shown beside units, calls and POIs of that type in lists, pickers and on the map,
	/// and on the map pins of the role's members. A unit, call type or role can also have one icon per status or priority
	/// (<see cref="Variant"/>, M0289), shown on the map instead of the type's own icon while that status or priority holds.
	/// One row per type and slot;
	/// a new upload replaces the row under a new IconKey, so the anonymous icon URL is immutable and caches forever. No
	/// foreign keys: the type deletes and the department purge remove the rows.
	/// </summary>
	[Table("TypeIcons")]
	public class TypeIcon : IEntity
	{
		[Key]
		[Required]
		public string TypeIconId { get; set; }

		[Required]
		public int DepartmentId { get; set; }

		/// <summary><see cref="TypeIconKinds"/>.</summary>
		[Required]
		public int TypeKind { get; set; }

		/// <summary>The UnitTypeId, CallTypeId, PoiTypeId or PersonnelRoleId, per <see cref="TypeKind"/>.</summary>
		[Required]
		public int TypeId { get; set; }

		/// <summary>Opaque random key the anonymous icon endpoint is addressed by; mirrored on the type's IconKey.</summary>
		[Required]
		public string IconKey { get; set; }

		/// <summary>
		/// Null for the type's own icon. Otherwise the status or priority this icon stands in for (M0289), as built by
		/// <see cref="TypeIcons.PriorityVariant"/>, <see cref="TypeIcons.BuiltInStatusVariant"/> or
		/// <see cref="TypeIcons.CustomStatusVariant"/>: a call type's icon while its call has one priority, a unit type's or
		/// role's while the unit or member is in one status. Only the type's own icon is mirrored on the type.
		/// </summary>
		public string Variant { get; set; }

		public string ContentType { get; set; }

		public int Width { get; set; }

		public int Height { get; set; }

		public long ByteSize { get; set; }

		public string Checksum { get; set; }

		public byte[] Data { get; set; }

		public string UploadedByUserId { get; set; }

		public DateTime UploadedOn { get; set; }

		[NotMapped]
		public object IdValue
		{
			get { return TypeIconId; }
			set { TypeIconId = value?.ToString(); }
		}

		[NotMapped]
		public string TableName => "TypeIcons";

		[NotMapped]
		public string IdName => "TypeIconId";

		[NotMapped]
		public int IdType => 1;

		[NotMapped]
		public IEnumerable<string> IgnoredProperties => new string[] { "IdValue", "IdType", "TableName", "IdName" };
	}

	/// <summary>Builds the URLs clients load type icons from.</summary>
	public static class TypeIcons
	{
		/// <summary>Largest upload accepted, in bytes.</summary>
		public const int MaxUploadBytes = 1024 * 1024;

		/// <summary>Smallest accepted long edge, in px.</summary>
		public const int MinimumEdge = 16;

		/// <summary>Largest accepted edge, in px (checked from the header before decoding).</summary>
		public const int MaximumEdge = 4096;

		/// <summary>Edge of the stored square PNG, in px; also the smallest upload that is not enlarged.</summary>
		public const int StoredSize = 128;

		/// <summary>Edge the web map and the apps draw an uploaded icon at, in px.</summary>
		public const int MapSize = 32;

		/// <summary>Edge web lists draw an icon at, in px.</summary>
		public const int ListSize = 20;

		/// <summary>Path of the anonymous icon endpoint on the API host (v4 apps and the Dispatch console).</summary>
		public const string ApiIconPath = "/api/v4/TypeIcons/";

		/// <summary>Path of the same endpoint on the web app, so web pages load icons from their own origin.</summary>
		public const string WebIconPath = "/TypeIcons/";

		/// <summary>The uploaded icon's absolute URL on the API host, or null when there is no uploaded icon.</summary>
		public static string ApiIconUrl(string apiBaseUrl, string iconKey)
		{
			if (string.IsNullOrWhiteSpace(iconKey))
				return null;

			return $"{(apiBaseUrl ?? string.Empty).TrimEnd('/')}{ApiIconPath}{iconKey}";
		}

		public static string ApiIconUrl(string apiBaseUrl, UnitType type) => ApiIconUrl(apiBaseUrl, type?.IconKey);

		public static string ApiIconUrl(string apiBaseUrl, CallType type) => ApiIconUrl(apiBaseUrl, type?.IconKey);

		public static string ApiIconUrl(string apiBaseUrl, PoiType type) => ApiIconUrl(apiBaseUrl, type?.IconKey);

		public static string ApiIconUrl(string apiBaseUrl, PersonnelRole role) => ApiIconUrl(apiBaseUrl, role?.IconKey);

		/// <summary>The uploaded icon's root-relative URL on the web app, or null when there is no uploaded icon.</summary>
		public static string WebIconUrl(string iconKey)
		{
			if (string.IsNullOrWhiteSpace(iconKey))
				return null;

			return $"{WebIconPath}{iconKey}";
		}

		/// <summary>
		/// The uploaded icon's URL on the API host with a <paramref name="badgeStyle"/> badge drawn on it (the type's own icon
		/// standing in for a status or priority it has no icon of its own for); the plain URL when the style is null.
		/// </summary>
		public static string ApiIconUrl(string apiBaseUrl, string iconKey, string badgeStyle) =>
			WithBadge(ApiIconUrl(apiBaseUrl, iconKey), badgeStyle);

		/// <summary>The web app twin of <see cref="ApiIconUrl(string, string, string)"/>.</summary>
		public static string WebIconUrl(string iconKey, string badgeStyle) => WithBadge(WebIconUrl(iconKey), badgeStyle);

		private static string WithBadge(string url, string badgeStyle)
		{
			if (url == null || !MapIconStyles.IsStyle(badgeStyle))
				return url;

			return $"{url}?s={badgeStyle}&v={MapIconStyles.Version}";
		}

		/// <summary>Path of the anonymous built-in pin endpoint on the API host.</summary>
		public const string ApiPinPath = "/api/v4/TypeIcons/Pins/";

		/// <summary>Path of the same endpoint on the web app.</summary>
		public const string WebPinPath = "/TypeIcons/Pins/";

		/// <summary>Built-in pin a call takes when its type picked no built-in icon (the Call.png artwork).</summary>
		public const string CallPinBase = "call";

		/// <summary>Built-in pin a unit takes when its type picked no built-in icon (the Engine artwork).</summary>
		public const string UnitPinBase = "engine";

		/// <summary>Built-in pin a member takes (the Person artwork).</summary>
		public const string PersonPinBase = "person";

		/// <summary>The bundled icon name older clients show for a call, unit or person with no built-in icon of its type.</summary>
		public const string LegacyCallImage = "Call";
		public const string LegacyUnitImage = "Engine_Responding";
		public const string LegacyPersonImage = "Person_RespondingCall";

		/// <summary>The name of a built-in pin in a style: "fire-high", "ambulance-available", "person-onscene".</summary>
		public static string PinName(string pinBase, string style) => $"{pinBase}-{style}";

		/// <summary>
		/// Every built-in pin artwork a styled pin can be drawn from: the call and unit icons a type can pick plus the
		/// default call, unit and person pins.
		/// </summary>
		public static readonly IReadOnlyCollection<string> PinBases = new HashSet<string>(
			Enum.GetValues(typeof(MapIconTypes)).Cast<MapIconTypes>().Select(MapIcons.ConvertTypeToName).Where(x => !string.IsNullOrEmpty(x))
				.Concat(new[] { CallPinBase, UnitPinBase, PersonPinBase }), StringComparer.Ordinal);

		/// <summary>Splits a pin name into its artwork and style; false for anything that is not a known pin.</summary>
		public static bool TryParsePinName(string pinName, out string pinBase, out string style)
		{
			pinBase = null;
			style = null;

			if (string.IsNullOrEmpty(pinName) || pinName.Length > 64)
				return false;

			var dash = pinName.LastIndexOf('-');
			if (dash <= 0 || dash == pinName.Length - 1)
				return false;

			var candidateBase = pinName.Substring(0, dash);
			var candidateStyle = pinName.Substring(dash + 1);

			if (!PinBases.Contains(candidateBase) || !MapIconStyles.IsStyle(candidateStyle))
				return false;

			pinBase = candidateBase;
			style = candidateStyle;
			return true;
		}

		/// <summary>A styled built-in pin's absolute URL on the API host (a 32 x 37 PNG whose tip is the position).</summary>
		public static string ApiPinUrl(string apiBaseUrl, string pinName)
		{
			if (string.IsNullOrWhiteSpace(pinName))
				return null;

			return $"{(apiBaseUrl ?? string.Empty).TrimEnd('/')}{ApiPinPath}{pinName}?v={MapIconStyles.Version}";
		}

		/// <summary>A styled built-in pin's root-relative URL on the web app.</summary>
		public static string WebPinUrl(string pinName)
		{
			if (string.IsNullOrWhiteSpace(pinName))
				return null;

			return $"{WebPinPath}{pinName}?v={MapIconStyles.Version}";
		}

		// p = call priority value, s = built-in status value, o = a department's custom status option id.
		private static readonly Regex VariantFormat = new Regex("^[pso](0|[1-9][0-9]{0,8})$",RegexOptions.Compiled | RegexOptions.CultureInvariant);

		/// <summary>The icon slot of a call priority (its stored value: a built-in priority or the department's priority id).</summary>
		public static string PriorityVariant(int priority) => $"p{priority}";

		/// <summary>The icon slot of a built-in unit or personnel status value.</summary>
		public static string BuiltInStatusVariant(int status) => $"s{status}";

		/// <summary>The icon slot of one of the department's custom status options (its CustomStateDetailId).</summary>
		public static string CustomStatusVariant(int customStateDetailId) => $"o{customStateDetailId}";

		/// <summary>
		/// True when <paramref name="variant"/> is a slot <paramref name="kind"/> can have: priorities for call types,
		/// statuses for unit types and roles, none for POI types.
		/// </summary>
		public static bool IsValidVariant(TypeIconKinds kind, string variant)
		{
			if (string.IsNullOrEmpty(variant) || !VariantFormat.IsMatch(variant))
				return false;

			switch (kind)
			{
				case TypeIconKinds.CallType:
					return variant[0] == 'p';
				case TypeIconKinds.UnitType:
				case TypeIconKinds.PersonnelRole:
					return variant[0] == 's' || (variant[0] == 'o' && variant != "o0");
				default:
					return false;
			}
		}

		/// <summary>The built-in map icon name a unit type picked (e.g. "ambulance"), or null when it has none.</summary>
		public static string BuiltInIconName(UnitType type) => BuiltInIconName(type?.MapIconType);

		/// <summary>The built-in map icon name a call type picked (e.g. "fire"), or null when it has none.</summary>
		public static string BuiltInIconName(CallType type) => BuiltInIconName(type?.MapIconType);

		/// <summary>
		/// The department's role order: SortOrder, then name, then id, so roles never ordered (all 0) list alphabetically
		/// and the order is stable.
		/// </summary>
		public static IEnumerable<PersonnelRole> InRoleOrder(IEnumerable<PersonnelRole> roles)
		{
			if (roles == null)
				return Enumerable.Empty<PersonnelRole>();

			return roles.Where(r => r != null)
				.OrderBy(r => r.SortOrder)
				.ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
				.ThenBy(r => r.PersonnelRoleId);
		}

		/// <summary>
		/// The role whose icon a person shows on the map: the first of <paramref name="heldRoles"/>, in role order, with an
		/// uploaded icon; null when none has one (the map then uses the built-in person pin).
		/// </summary>
		public static PersonnelRole MapIconRole(IEnumerable<PersonnelRole> heldRoles) =>
			InRoleOrder(heldRoles).FirstOrDefault(r => !string.IsNullOrWhiteSpace(r.IconKey));

		private static string BuiltInIconName(int? mapIconType)
		{
			if (!mapIconType.HasValue || mapIconType.Value < 0)
				return null;

			var name = MapIcons.ConvertTypeToName((MapIconTypes)mapIconType.Value);
			return string.IsNullOrEmpty(name) ? null : name;
		}
	}
}
