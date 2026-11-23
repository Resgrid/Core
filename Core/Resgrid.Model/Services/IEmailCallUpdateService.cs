using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Identity;

namespace Resgrid.Model.Services
{
	/// <summary>One inbound dispatch email for <see cref="IEmailCallUpdateService.ProcessAsync"/>.</summary>
	public class EmailCallUpdateRequest
	{
		public int DepartmentId { get; set; }

		/// <summary>Optional; loaded when null (its managing user writes the update notes).</summary>
		public Department Department { get; set; }

		/// <summary>The email format (<see cref="CallEmailTypes"/>) that read the email; picks the built-in preset when nothing is saved.</summary>
		public int? FormatType { get; set; }

		public CallEmail Email { get; set; }

		public CallEmailParseResult Parsed { get; set; }

		public CallEmailUpdateSources Source { get; set; }

		public string IntakeChannelId { get; set; }

		/// <summary>The department's config when the caller already has it; read from the setting otherwise.</summary>
		public EmailCallUpdateConfig Config { get; set; }

		/// <summary>How long to wait for another email with the same CAD call ID; default 10 seconds.</summary>
		public TimeSpan? LockTimeout { get; set; }
	}

	/// <summary>One field group a CAD email touched, as written in the update note.</summary>
	public class EmailCallFieldChange
	{
		/// <summary>Location, Priority, Type, Name, Nature, Notes or Resources.</summary>
		public string Group { get; set; }

		public string OldValue { get; set; }

		public string NewValue { get; set; }

		/// <summary>True when the call took the value; false when it was only recorded.</summary>
		public bool Applied { get; set; }

		/// <summary>Why a value was not applied: "Policy" (department setting), "EditedInResgrid" or "Protected".</summary>
		public string Reason { get; set; }
	}

	/// <summary>What <see cref="IEmailCallUpdateService.ProcessAsync"/> did.</summary>
	public class EmailCallUpdateResult
	{
		public CallEmailUpdateOutcomes Outcome { get; set; }

		/// <summary>The call created, updated, closed or noted; null for <see cref="CallEmailUpdateOutcomes.CloseUnmatched"/> and Busy.</summary>
		public Call Call { get; set; }

		/// <summary>The CAD call ID the email carried (normalised), or null.</summary>
		public string CadCallId { get; set; }

		public List<EmailCallFieldChange> Changes { get; set; } = new List<EmailCallFieldChange>();

		/// <summary>The whole call was paged again.</summary>
		public bool Repaged { get; set; }

		/// <summary>Resources the update added to the call.</summary>
		public int AddedResources { get; set; }

		/// <summary>True when the caller's create delegate ran (<see cref="CallEmailUpdateOutcomes.NotApplicable"/> or Created).</summary>
		public bool CreatedNewCall => Outcome == CallEmailUpdateOutcomes.NotApplicable || Outcome == CallEmailUpdateOutcomes.Created;
	}

	/// <summary>What the settings page's test box shows for a pasted email.</summary>
	public class EmailCallUpdatePreview
	{
		public bool FormatFoundCall { get; set; }
		public string CadCallId { get; set; }
		public CadEmailCloseKinds CloseKind { get; set; }
		public string Name { get; set; }
		public string Nature { get; set; }
		public string Address { get; set; }
		public string Coordinates { get; set; }
		public string Type { get; set; }
		public string Priority { get; set; }
		public List<string> Units { get; set; } = new List<string>();
		public string Notes { get; set; }
		public bool UpdatesEnabled { get; set; }
		public bool IsBuiltInPreset { get; set; }
	}

	/// <summary>
	/// CAD email call updates (cad-email-call-updates-plan.md): decides whether an inbound dispatch email creates a call or updates,
	/// closes or is noted on the call that already carries its CAD call ID, under the department's <see cref="EmailCallUpdateConfig"/>,
	/// and keeps the update ledger. Used by the dispatch and group dispatch email addresses, the mailbox importer and intake email
	/// channels that opt in.
	/// </summary>
	public interface IEmailCallUpdateService
	{
		/// <summary>
		/// The email as the format reads it (<see cref="ICallEmailFactory.ParseCallEmailAsync"/>), with the department's labels for the
		/// Labeled Fields format. Null when the format failed or is unknown.
		/// </summary>
		Task<CallEmailParseResult> ParseAsync(int formatType, CallEmail email, string managingUserId, List<IdentityUser> users, Department department,
			List<Unit> units, int priority, List<DepartmentCallPriority> priorities, List<CallType> callTypes, CancellationToken cancellationToken = default);

		/// <summary>
		/// Handles one parsed email. When it needs a new call (updates off, no CAD call ID, or no call has the ID) it calls
		/// <paramref name="createCall"/> with the parsed call, which saves and dispatches it exactly as before and returns the saved
		/// call; the created call carries the ID. Otherwise it updates, closes or notes the matched call itself, pages per the
		/// department's rule and raises the refresh. Returns <see cref="CallEmailUpdateOutcomes.Busy"/> (nothing done) when another
		/// email with the same ID held the lock too long. Exceptions from <paramref name="createCall"/> propagate.
		/// </summary>
		Task<EmailCallUpdateResult> ProcessAsync(EmailCallUpdateRequest request, Func<Call, Task<Call>> createCall, CancellationToken cancellationToken = default);

		/// <summary>
		/// The ledger baseline for a call created outside <see cref="ProcessAsync"/> after it answered
		/// <see cref="CallEmailUpdateOutcomes.Created"/> without one (intake email channels make their own pending call). Never throws.
		/// </summary>
		Task RecordCreatedAsync(EmailCallUpdateRequest request, Call saved, string cadCallId);

		/// <summary>
		/// The settings page's test box: what the department's format (and its labels or the given unsaved ones) reads from a pasted
		/// email, the CAD call ID and whether it reads as a close or cancel. Nothing is created or matched.
		/// </summary>
		Task<EmailCallUpdatePreview> PreviewAsync(int departmentId, int formatType, string subject, string body,
			EmailCallUpdateConfig config = null, EmailLabeledFieldsConfig labeledFields = null, CancellationToken cancellationToken = default);
	}
}
