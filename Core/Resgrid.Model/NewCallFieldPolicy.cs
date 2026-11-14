using System;
using System.Collections.Generic;
using System.ComponentModel;
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
		public const string MapPage = "mapPage";
		public const string PrimaryContact = "primaryContact";
		public const string AdditionalContacts = "additionalContacts";
		public const string CheckInTimers = "checkInTimers";
		public const string Part2Consent = "part2Consent";
		public const string PendingCall = "pendingCall";

		/// <summary>
		/// The dispatch recipients section as a whole. Its requirement means "at least one recipient". Its visibility is
		/// not set on its own: the section shows while any of <see cref="DispatchTargets"/> does (see
		/// <see cref="NewCallFieldPolicy.IsVisible"/>), and the stored rule follows them so a client that only knows this key
		/// hides its picker exactly when every list is hidden.
		/// </summary>
		public const string DispatchList = "dispatchList";

		public const string DispatchPersonnel = "dispatchPersonnel";
		public const string DispatchGroups = "dispatchGroups";
		public const string DispatchRoles = "dispatchRoles";
		public const string DispatchUnits = "dispatchUnits";

		/// <summary>The four recipient lists a call can be dispatched to, each of which can be hidden on its own.</summary>
		public static readonly IReadOnlyList<string> DispatchTargets = new List<string>
		{
			DispatchPersonnel,
			DispatchGroups,
			DispatchRoles,
			DispatchUnits
		};

		/// <summary>Every configurable key, in the order the admin screen should list them.</summary>
		public static readonly IReadOnlyList<string> All = new List<string>
		{
			Address,
			Geolocation,
			What3Words,
			PlusCode,
			DestinationPoi,
			IndoorLocation,
			MapPage,
			Note,
			ContactName,
			ContactInfo,
			PrimaryContact,
			AdditionalContacts,
			ExternalId,
			IncidentId,
			ReferenceId,
			Protocols,
			LinkedCall,
			DispatchPersonnel,
			DispatchGroups,
			DispatchRoles,
			DispatchUnits,
			DispatchList,
			CheckInTimers,
			Part2Consent,
			DispatchOn,
			PendingCall
		};

		/// <summary>
		/// Fields that can be shown or hidden but never required. A plus code is only a way of finding the location; it is
		/// never stored on the call, so requiring it guarantees the crews nothing (require GPS coordinates instead). The
		/// check-in timers, Part 2 consent and save-as-pending inputs are switches whose "off" is an answer, and a single
		/// recipient list is covered by requiring the recipients as a whole (<see cref="DispatchList"/>), which any list
		/// satisfies.
		/// </summary>
		private static readonly IReadOnlyList<string> HideOnly = new List<string>
		{
			PlusCode,
			AdditionalContacts,
			CheckInTimers,
			Part2Consent,
			PendingCall,
			DispatchPersonnel,
			DispatchGroups,
			DispatchRoles,
			DispatchUnits
		};

		public static bool IsKnown(string key) =>
			!string.IsNullOrWhiteSpace(key) && All.Any(x => string.Equals(x, key, StringComparison.OrdinalIgnoreCase));

		/// <summary>True for one of the four recipient lists (<see cref="DispatchTargets"/>).</summary>
		public static bool IsDispatchTarget(string key) =>
			!string.IsNullOrWhiteSpace(key) && DispatchTargets.Any(x => string.Equals(x, key, StringComparison.OrdinalIgnoreCase));

		/// <summary>False for a field that can be shown or hidden but never required; see <see cref="HideOnly"/>.</summary>
		public static bool CanBeRequired(string key) =>
			IsKnown(key) && !HideOnly.Any(x => string.Equals(x, key, StringComparison.OrdinalIgnoreCase));

		/// <summary>
		/// False for <see cref="DispatchList"/>, whose visibility follows the four recipient lists rather than being set.
		/// </summary>
		public static bool CanBeHidden(string key) =>
			IsKnown(key) && !string.Equals(key, DispatchList, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>Visibility and requiredness for one built-in new-call field.</summary>
	[ProtoContract]
	public class NewCallFieldRule
	{
		[ProtoMember(1)]
		public string Key { get; set; }

		/// <summary>False hides the field from every call-creation surface.</summary>
		/// <remarks>
		/// DefaultValue must match the initializer. Without it protobuf-net treats false as the implicit
		/// zero default and never writes it, so a hidden field read back as visible and the rule was lost.
		/// </remarks>
		[DefaultValue(true)]
		[ProtoMember(2)]
		public bool Visible { get; set; } = true;

		/// <summary>True blocks creating (or saving an edit to) a call until the field has a value.</summary>
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
		/// <remarks>
		/// The recipients section (<see cref="NewCallFieldKeys.DispatchList"/>) shows while any of its four lists does. A
		/// list with no rule of its own follows the section's stored rule: before the lists could be hidden one by one,
		/// hiding the section hid all of them, and a policy saved then must keep doing so.
		/// </remarks>
		public bool IsVisible(string key)
		{
			if (string.Equals(key, NewCallFieldKeys.DispatchList, StringComparison.OrdinalIgnoreCase))
			{
				if (NewCallFieldKeys.DispatchTargets.Any(x => Find(x) != null))
					return NewCallFieldKeys.DispatchTargets.Any(IsVisible);

				return Find(key)?.Visible ?? true;
			}

			var rule = Find(key);

			if (rule != null)
				return rule.Visible;

			if (NewCallFieldKeys.IsDispatchTarget(key))
				return Find(NewCallFieldKeys.DispatchList)?.Visible ?? true;

			return true;
		}

		/// <summary>
		/// True when every recipient list is hidden: the call forms then offer an optional "All Call" (dispatch everyone)
		/// instead, and a call left without it is informational and pages nobody.
		/// </summary>
		public bool AreAllDispatchTargetsHidden => !NewCallFieldKeys.DispatchTargets.Any(IsVisible);

		/// <summary>
		/// True when the field must have a value. Hidden fields, and fields that can never be required
		/// (<see cref="NewCallFieldKeys.CanBeRequired"/>), are not required whatever the stored rule says —
		/// otherwise a bad configuration would lock the department out of creating calls.
		/// </summary>
		public bool IsRequired(string key)
		{
			var rule = Find(key);

			if (rule == null)
				return false;

			return rule.Required && NewCallFieldKeys.CanBeRequired(key) && IsVisible(key);
		}

		/// <summary>True when nothing is configured, i.e. stock behaviour.</summary>
		public bool IsEmpty => Rules == null || Rules.Count == 0;

		/// <summary>
		/// Drops rules for keys we do not recognise and rules that say nothing (visible, not required),
		/// so what gets stored stays small and a renamed key cannot linger forever.
		/// </summary>
		/// <remarks>
		/// The recipients section's own rule is rewritten to follow its four lists (shown while any list is), so a client
		/// that predates the per-list keys and reads only <see cref="NewCallFieldKeys.DispatchList"/> hides its whole
		/// picker exactly when the department hid every list. Each list's visibility is worked out before that, with the
		/// fallback <see cref="IsVisible"/> applies, and written down when it is hidden.
		/// </remarks>
		public NewCallFieldPolicy Normalize()
		{
			Rules = (Rules ?? new List<NewCallFieldRule>())
				.Where(x => NewCallFieldKeys.IsKnown(x?.Key))
				.GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
				.Select(g => g.Last())
				.Select(x =>
				{
					// A policy saved before plus code stopped being requirable keeps its visibility but loses the requirement.
					if (x.Required && !NewCallFieldKeys.CanBeRequired(x.Key))
						x.Required = false;

					return x;
				})
				.ToList();

			var targetVisibility = NewCallFieldKeys.DispatchTargets.ToDictionary(x => x, IsVisible);

			foreach (var target in targetVisibility)
			{
				var rule = Find(target.Key);

				if (rule == null)
					Rules.Add(new NewCallFieldRule { Key = target.Key, Visible = target.Value });
				else
					rule.Visible = target.Value;
			}

			var section = Find(NewCallFieldKeys.DispatchList);

			if (section == null)
				Rules.Add(new NewCallFieldRule { Key = NewCallFieldKeys.DispatchList, Visible = targetVisibility.Values.Any(x => x) });
			else
				section.Visible = targetVisibility.Values.Any(x => x);

			Rules = Rules
				.Where(x => !x.Visible || x.Required)
				.ToList();

			return this;
		}
	}
}
