using System;

namespace Resgrid.Model
{
	/// <summary>The call fields an email format actually read from an email (not defaults it filled in).</summary>
	[Flags]
	public enum CallEmailParsedFields
	{
		None = 0,
		Address = 1,
		Coordinates = 2,
		Priority = 4,
		Type = 8,
		Name = 16,
		Nature = 32,
		Notes = 64,
		Resources = 128,
		What3Words = 256
	}

	/// <summary>
	/// What an email format made of an inbound dispatch email: always a new call (formats no longer look for an existing one),
	/// the format's own key for "the same incident", and which fields it read. The CAD update engine decides whether the email
	/// creates that call or updates an open one (plan cad-email-call-updates-plan.md §4.2).
	/// </summary>
	public class CallEmailParseResult
	{
		/// <summary>The call the email describes, never saved (<see cref="Call.CallId"/> 0). Null when the format found no call.</summary>
		public Call Call { get; set; }

		/// <summary>The format's key for the incident (its incident number or external identifier), or null.</summary>
		public string MatchKey { get; set; }

		/// <summary>
		/// True when <see cref="MatchKey"/> is not a value a person would recognise (Parkland County 2: call name plus call time),
		/// so it is kept only in the update ledger, never written to the call's external identifier.
		/// </summary>
		public bool MatchKeyIsComposite { get; set; }

		/// <summary>The fields the format read from the email.</summary>
		public CallEmailParsedFields ParsedFields { get; set; }

		/// <summary>
		/// Parkland County 2 only, for open calls created before the update ledger existed: the comparison that format used to
		/// find its own calls (name and call time). Null for every other format.
		/// </summary>
		public Func<Call, bool> LegacyMatch { get; set; }

		public bool Has(CallEmailParsedFields field) => (ParsedFields & field) == field;
	}

	/// <summary>
	/// Optional on an <see cref="ICallEmailTemplate"/>: a format that reports exactly which fields it read and its match key.
	/// Formats without it are described from the call they build (<c>CallEmailFactory</c>).
	/// </summary>
	public interface ICallEmailParser
	{
		/// <summary>The email as a parse result, built from the same inputs as <see cref="ICallEmailTemplate.GenerateCall"/>.</summary>
		System.Threading.Tasks.Task<CallEmailParseResult> ParseAsync(CallEmail email, string managingUser, System.Collections.Generic.List<Identity.IdentityUser> users,
			Department department, System.Collections.Generic.List<Unit> units, int priority, System.Collections.Generic.List<DepartmentCallPriority> activePriorities,
			System.Collections.Generic.List<CallType> callTypes, Providers.IGeoLocationProvider geolocationProvider, EmailLabeledFieldsConfig labeledFields);
	}
}
