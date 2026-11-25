using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Member wellness WL0 (protocols-training-knowledge-plan §13b): the per-member exposure and critical-incident log, post-call
	/// flags and the suggestions built from them, at-risk screening flags with follow-up tasks on Records work assignments,
	/// opt-in anniversary reminders, the member's own export and the 30-year retention.
	/// <para>
	/// The restricted tier: a member always reads and exports their own entries; a wellness officer (a member of the role named in
	/// <see cref="WellnessSettings.WellnessOfficerRoleId"/>) reads everyone's and keeps screening flags. Department administration
	/// alone opens nothing beyond the settings. Every read and write refuses a caller outside the tier with
	/// <see cref="UnauthorizedAccessException"/>; a rule refusal throws <see cref="MemberWellnessException"/>.
	/// </para>
	/// </summary>
	public interface IMemberWellnessService
	{
		// ---- Access ---------------------------------------------------------------------------------------------------------

		Task<WellnessSettings> GetSettingsAsync(int departmentId);

		/// <summary>Department administrators only. Audited.</summary>
		Task<WellnessSettings> SaveSettingsAsync(int departmentId, string adminUserId, WellnessSettings settings, CancellationToken cancellationToken = default);

		/// <summary>A current member holding the department's wellness officer role. Administration alone is not enough.</summary>
		Task<bool> IsWellnessOfficerAsync(int departmentId, string userId);

		Task<bool> IsDepartmentAdminAsync(int departmentId, string userId);

		/// <summary>A wellness officer, a department administrator, or someone who may edit the call.</summary>
		Task<bool> CanFlagCallAsync(int departmentId, string userId, int callId);

		/// <summary>Active members holding a valid certification of the peer support team's type (empty when none is set).</summary>
		Task<List<string>> GetPeerTeamUserIdsAsync(int departmentId);

		// ---- Entries --------------------------------------------------------------------------------------------------------

		/// <summary>One member's log: the member themselves or a wellness officer. Protected values are revealed for the caller's grant.</summary>
		Task<MemberWellnessOverview> GetOverviewAsync(int departmentId, string viewerUserId, string memberUserId);

		/// <summary>One entry, revealed; null when it does not exist or the viewer may not read it.</summary>
		Task<RmsResponderExposure> GetEntryAsync(int departmentId, string viewerUserId, string exposureId);

		/// <summary>Logs an entry as confirmed: a member for themselves, a wellness officer for any current member.</summary>
		Task<RmsResponderExposure> RecordAsync(int departmentId, string actorUserId, ResponderExposureInput input, CancellationToken cancellationToken = default);

		/// <summary>Edits an entry (a suggestion or a confirmed one); the member or a wellness officer.</summary>
		Task<RmsResponderExposure> UpdateAsync(int departmentId, string actorUserId, string exposureId, ResponderExposureInput input, long? expectedRowVersion, CancellationToken cancellationToken = default);

		/// <summary>Confirms a suggestion, optionally with the details the confirmer adds. Only a person confirms; nothing confirms itself.</summary>
		Task<RmsResponderExposure> ConfirmAsync(int departmentId, string actorUserId, string exposureId, ResponderExposureInput details, CancellationToken cancellationToken = default);

		Task<RmsResponderExposure> DismissAsync(int departmentId, string actorUserId, string exposureId, CancellationToken cancellationToken = default);

		/// <summary>Marks a confirmed entry as recorded in error. It is kept for its retention period and no longer counted.</summary>
		Task<RmsResponderExposure> VoidAsync(int departmentId, string actorUserId, string exposureId, CancellationToken cancellationToken = default);

		/// <summary>The member's own opt-in to anniversary reminders for a confirmed critical incident (to themselves and/or the peer team).</summary>
		Task<RmsResponderExposure> SetRemindersAsync(int departmentId, string memberUserId, string exposureId, bool remindMember, bool remindPeerTeam, CancellationToken cancellationToken = default);

		/// <summary>The member's own entries for their export (PDF/CSV); never anyone else's. Audited.</summary>
		Task<List<RmsResponderExposure>> GetOwnEntriesForExportAsync(int departmentId, string memberUserId);

		/// <summary>The member's own entries for the unattended GDPR archive (sealed values stay sealed; the archive redacts them).</summary>
		Task<object> BuildGdprExportAsync(string userId, int departmentId);

		// ---- Officer --------------------------------------------------------------------------------------------------------

		/// <summary>Per-member operational counts for the officer report (wellness officers only).</summary>
		Task<List<MemberExposureSummary>> GetDepartmentSummaryAsync(int departmentId, string officerUserId, DateTime? fromUtc, DateTime? toUtc);

		/// <summary>Every entry in the window with operational columns only (protected values blanked), for the officer report export.</summary>
		Task<List<RmsResponderExposure>> GetDepartmentEntriesAsync(int departmentId, string officerUserId, DateTime? fromUtc, DateTime? toUtc);

		// ---- Post-call flags ------------------------------------------------------------------------------------------------

		Task<RmsCallExposureFlag> FlagCallAsync(int departmentId, string actorUserId, int callId, int category, CancellationToken cancellationToken = default);

		/// <summary>Clears a flag; its suggestions nobody confirmed are removed, confirmed entries stay.</summary>
		Task ClearCallFlagAsync(int departmentId, string actorUserId, string flagId, CancellationToken cancellationToken = default);

		Task<List<RmsCallExposureFlag>> GetCallFlagsAsync(int departmentId, string actorUserId, int callId);

		Task<List<RmsCallExposureFlag>> GetRecentFlagsAsync(int departmentId, string officerUserId, int take);

		// ---- Screening ------------------------------------------------------------------------------------------------------

		Task<RmsResponderScreeningFlag> SetScreeningFlagAsync(int departmentId, string officerUserId, string memberUserId, int? category, DateTime? reviewOn, string planNotes, CancellationToken cancellationToken = default);

		Task ClearScreeningFlagAsync(int departmentId, string officerUserId, string flagId, CancellationToken cancellationToken = default);

		/// <summary>A follow-up task on Records work assignments (purpose wellness_follow_up), addressed to the member or an officer.</summary>
		Task<RmsRecordWorkAssignment> AddFollowUpAsync(int departmentId, string officerUserId, string flagId, string kind, string assigneeUserId, DateTime? dueOn, CancellationToken cancellationToken = default);

		/// <summary>The assignee or a wellness officer.</summary>
		Task<RmsRecordWorkAssignment> CompleteFollowUpAsync(int departmentId, string actorUserId, string assignmentId, CancellationToken cancellationToken = default);

		Task<RmsRecordWorkAssignment> CancelFollowUpAsync(int departmentId, string officerUserId, string assignmentId, CancellationToken cancellationToken = default);

		// ---- Worker 91 ------------------------------------------------------------------------------------------------------

		/// <summary>Builds the suggestions of flags whose call has ended (or that waited long enough). Starts from the pending-flag query.</summary>
		Task<WellnessRunResult> ProcessPendingFlagsAsync(DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>Sends due opt-in anniversary reminders. Starts from the departments with due anniversaries; claims each before sending.</summary>
		Task<int> SendAnniversaryRemindersAsync(DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>Erases entries past their retention (never under 30 years) that no legal hold covers.</summary>
		Task<int> EraseExpiredAsync(DateTime utcNow, CancellationToken cancellationToken = default);
	}
}
