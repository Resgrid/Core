using System;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

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
	/// A department's uploaded icon for one unit, call or POI type (migration M0285) or personnel role (M0286): the server
	/// re-encoded, square, transparent PNG shown beside units, calls and POIs of that type in lists, pickers and on the map,
	/// and on the map pins of the role's members. One row per type;
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
