using ProtoBuf;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Resgrid.Model
{
	/// <summary>
	/// A department's catalog of response codes (DepartmentSettingTypes.DispatchResponseCodes): a category per proposed
	/// resource, such as "Code 3" (lights and siren) or "Code 2". Optional and off by default: with no codes the feature
	/// is invisible. Codes appear to dispatchers on run card proposals; crews see them only when
	/// <see cref="ShowToCrews"/> is on.
	/// </summary>
	[ProtoContract]
	public class DispatchResponseCodesConfig
	{
		public const int MaximumCodes = 50;
		public const int MaximumCodeLength = 16;
		public const int MaximumNameLength = 100;
		public const int MaximumDescriptionLength = 500;

		private static readonly Regex HexColor = new Regex("^#(?:[0-9A-Fa-f]{3}|[0-9A-Fa-f]{4}|[0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$", RegexOptions.Compiled);

		/// <summary>
		/// The badge colour as stored: a hex colour ("#d9534f"), or null for anything else. Clients put it in a style attribute,
		/// so nothing but a colour may get through.
		/// </summary>
		public static string NormalizeColor(string color)
		{
			var trimmed = color?.Trim();
			return !string.IsNullOrEmpty(trimmed) && HexColor.IsMatch(trimmed) ? trimmed : null;
		}

		[ProtoMember(1)]
		public List<DispatchResponseCode> Codes { get; set; } = new List<DispatchResponseCode>();

		/// <summary>Carry the code onto unit dispatches, the unit push title and the field apps. Off by default.</summary>
		[ProtoMember(2)]
		public bool ShowToCrews { get; set; }

		public DispatchResponseCode Find(int? id)
		{
			if (!id.HasValue || Codes == null)
				return null;

			return Codes.FirstOrDefault(c => c != null && c.Id == id.Value);
		}

		public IEnumerable<DispatchResponseCode> ActiveCodes()
		{
			return (Codes ?? new List<DispatchResponseCode>()).Where(c => c != null && !c.Retired).OrderBy(c => c.SortOrder).ThenBy(c => c.Code);
		}
	}

	[ProtoContract]
	public class DispatchResponseCode
	{
		/// <summary>Stable id within the department's catalog; rows and dispatches refer to it.</summary>
		[ProtoMember(1)]
		public int Id { get; set; }

		/// <summary>Short code shown on badges and push titles ("C3", "MUG1").</summary>
		[ProtoMember(2)]
		public string Code { get; set; }

		[ProtoMember(3)]
		public string Name { get; set; }

		[ProtoMember(4)]
		public string Description { get; set; }

		/// <summary>Badge colour (hex), optional.</summary>
		[ProtoMember(5)]
		public string Color { get; set; }

		[ProtoMember(6)]
		public int SortOrder { get; set; }

		/// <summary>Retired codes stay readable on history but cannot be picked.</summary>
		[ProtoMember(7)]
		public bool Retired { get; set; }
	}
}
