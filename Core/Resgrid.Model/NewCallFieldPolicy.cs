using System;
using System.Collections.Generic;
using System.Linq;
using ProtoBuf;

namespace Resgrid.Model
{
	/// <summary>
	/// The built-in new-call fields a department can hide or make mandatory.
	/// </summary>
	/// <remarks>
	/// Values are the wire contract shared with every client, so they are stable strings rather than
	/// enum ordinals. Only fields a department could plausibly not use are listed: Name, Nature,
	/// Priority and Type are load-bearing for dispatch, matching and reporting and are never
	/// configurable.
	/// </remarks>
	public static class NewCallFieldKeys
	{
		public const string Note = "note";
		public const string Address = "address";
		public const string Geolocation = "geolocation";
		public const string What3Words = "what3words";
		public const string PlusCode = "pluscode";
		public const string ContactName = "contactName";
		public const string ContactInfo = "contactInfo";
		public const string ExternalId = "externalId";
		public const string IncidentId = "incidentId";
		public const string ReferenceId = "referenceId";
		public const string DestinationPoi = "destinationPoi";
		public const string IndoorLocation = "indoorLocation";
		public const string Protocols = "protocols";
		public const string LinkedCall = "linkedCall";
		public const string DispatchOn = "dispatchOn";
		public const string DispatchList = "dispatchList";

		/// <summary>Every configurable key, in the order the admin screen should list them.</summary>
		public static readonly IReadOnlyList<string> All = new List<string>
		{
			Address,
			Geolocation,
			What3Words,
			PlusCode,
			DestinationPoi,
			IndoorLocation,
			Note,
			ContactName,
			ContactInfo,
			ExternalId,
			IncidentId,
			ReferenceId,
			Protocols,
			LinkedCall,
			DispatchOn,
			DispatchList
		};

		public static bool IsKnown(string key) =>
			!string.IsNullOrWhiteSpace(key) && All.Any(x => string.Equals(x, key, StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>Visibility and requiredness for one built-in new-call field.</summary>
	[ProtoContract]
	public class NewCallFieldRule
	{
		[ProtoMember(1)]
		public string Key { get; set; }

		/// <summary>False hides the field from every call-creation surface.</summary>
		[ProtoMember(2)]
		public bool Visible { get; set; } = true;

		/// <summary>True blocks call creation until the field has a value.</summary>
		[ProtoMember(3)]
		public bool Required { get; set; }
	}

	/// <summary>
	/// A department's new-call form policy: which built-in fields appear, and which must be filled in
	/// before a call can be created and forwarded to the field.
	/// </summary>
	/// <remarks>
	/// The default (no stored policy, or a key with no rule) is "visible, not required" — exactly how
	/// Resgrid behaved before this existed, so a department that never configures anything sees no
	/// change.
	///
	/// A hidden field is implicitly not required: requiring something nobody can fill in would make
	/// call creation impossible, so <see cref="IsRequired"/> refuses that combination rather than
	/// trusting stored data to be sane.
	/// </remarks>
	[ProtoContract]
	public class NewCallFieldPolicy
	{
		[ProtoMember(1)]
		public List<NewCallFieldRule> Rules { get; set; } = new List<NewCallFieldRule>();

		private NewCallFieldRule Find(string key) =>
			Rules?.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));

		/// <summary>True when the field should be shown. Unknown or unconfigured fields are shown.</summary>
		public bool IsVisible(string key)
		{
			var rule = Find(key);

			return rule?.Visible ?? true;
		}

		/// <summary>
		/// True when the field must have a value. Hidden fields are never required, whatever the stored
		/// rule says — otherwise a bad configuration would lock the department out of creating calls.
		/// </summary>
		public bool IsRequired(string key)
		{
			var rule = Find(key);

			if (rule == null)
				return false;

			return rule.Visible && rule.Required;
		}

		/// <summary>True when nothing is configured, i.e. stock behaviour.</summary>
		public bool IsEmpty => Rules == null || Rules.Count == 0;

		/// <summary>
		/// Drops rules for keys we do not recognise and rules that say nothing (visible, not required),
		/// so what gets stored stays small and a renamed key cannot linger forever.
		/// </summary>
		public NewCallFieldPolicy Normalize()
		{
			Rules = (Rules ?? new List<NewCallFieldRule>())
				.Where(x => NewCallFieldKeys.IsKnown(x?.Key))
				.GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
				.Select(g => g.Last())
				.Where(x => !x.Visible || x.Required)
				.ToList();

			return this;
		}
	}
}
