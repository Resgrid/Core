using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Member wellness WL1 (protocols-training-knowledge-plan §13b): the BBP / infectious-disease exposure case (designated officer, Ryan White
	/// notification tracking, post-exposure follow-up on Records work assignments), injury → return to duty with the OSHA 300/301 shapes, and
	/// the NFPA 1582 medical clearance with its restriction status.
	/// <para>
	/// The restricted tier, as WL0: a member always reads their own records and reports their own exposure or injury; the designated officer
	/// (the role in <see cref="WellnessSettings.DesignatedOfficerRoleId"/>, or the wellness officer role when none is named) runs exposure
	/// cases; health officers (designated or wellness officers) keep injuries, duty status and clearances and export the OSHA log. Department
	/// and group administrators see duty status and clearance status only (<see cref="MemberDutyStatus"/>), never why. Department
	/// administration alone opens no record. A caller outside the tier gets <see cref="UnauthorizedAccessException"/>; a rule refusal throws
	/// <see cref="MemberWellnessException"/>. Nothing here is indexed for search or sent to any AI feature (§16.3).
	/// </para>
	/// </summary>
	public interface IMemberHealthService
	{
		// ---- Access ---------------------------------------------------------------------------------------------------------

		Task<MemberHealthAccess> GetAccessAsync(int departmentId, string userId);

		/// <summary>A current member of the designated officer role (or, when none is named, of the wellness officer role).</summary>
		Task<bool> IsDesignatedOfficerAsync(int departmentId, string userId);

		/// <summary>A designated officer or a wellness officer.</summary>
		Task<bool> IsHealthOfficerAsync(int departmentId, string userId);

		// ---- BBP / infectious-disease exposure cases ------------------------------------------------------------------------

		/// <summary>
		/// Opens a case: from the member's confirmed WL0 entry, or by logging a new one. The member themselves or a designated officer. The
		/// designated officers are told (EventType 68). One open case per WL0 entry.
		/// </summary>
		Task<RmsExposureIncident> ReportExposureAsync(int departmentId, string actorUserId, ExposureIncidentReport report, CancellationToken cancellationToken = default);

		/// <summary>One case, revealed for the caller's grant; null when it does not exist or the viewer may not read it.</summary>
		Task<RmsExposureIncident> GetCaseAsync(int departmentId, string viewerUserId, string incidentId);

		/// <summary>A member's cases: the member themselves or a designated officer.</summary>
		Task<List<RmsExposureIncident>> GetCasesForMemberAsync(int departmentId, string viewerUserId, string memberUserId);

		/// <summary>The department's cases (designated officers only), operational columns only.</summary>
		Task<List<RmsExposureIncident>> GetDepartmentCasesAsync(int departmentId, string officerUserId, bool includeClosed);

		/// <summary>Moves a case one step along <see cref="ExposureIncidentWorkflow"/> (designated officers only). Closing needs every follow-up done.</summary>
		Task<RmsExposureIncident> AdvanceCaseAsync(int departmentId, string officerUserId, string incidentId, ExposureIncidentStep step, CancellationToken cancellationToken = default);

		/// <summary>Updates the case's facility and protected notes without moving it (designated officers only).</summary>
		Task<RmsExposureIncident> UpdateCaseNotesAsync(int departmentId, string officerUserId, string incidentId, string sourceFacility, string sourceFollowUpNotes,
			string notificationResponse, string medicalNotes, long? expectedRowVersion, CancellationToken cancellationToken = default);

		/// <summary>The case's post-exposure follow-up tasks (Records work assignments, purpose exposure_case_follow_up).</summary>
		Task<List<RmsRecordWorkAssignment>> GetCaseFollowUpsAsync(int departmentId, string viewerUserId, string incidentId);

		Task<RmsRecordWorkAssignment> AddCaseFollowUpAsync(int departmentId, string officerUserId, string incidentId, string kind, string assigneeUserId, DateTime? dueOn,
			CancellationToken cancellationToken = default);

		/// <summary>The assignee or a designated officer.</summary>
		Task<RmsRecordWorkAssignment> CompleteCaseFollowUpAsync(int departmentId, string actorUserId, string assignmentId, CancellationToken cancellationToken = default);

		Task<RmsRecordWorkAssignment> CancelCaseFollowUpAsync(int departmentId, string officerUserId, string assignmentId, CancellationToken cancellationToken = default);

		/// <summary>The member's BBP training status (§13b.3), from the certification type named in the wellness settings.</summary>
		Task<BbpTrainingStatus> GetBbpTrainingStatusAsync(int departmentId, string memberUserId);

		// ---- Injuries and duty status ---------------------------------------------------------------------------------------

		/// <summary>Reports an injury: the member for themselves (the OSHA facts wait for an officer) or a health officer for any current member.</summary>
		Task<RmsMemberInjury> ReportInjuryAsync(int departmentId, string actorUserId, MemberInjuryInput input, CancellationToken cancellationToken = default);

		Task<RmsMemberInjury> GetInjuryAsync(int departmentId, string viewerUserId, string injuryId);

		Task<List<RmsMemberInjury>> GetInjuriesForMemberAsync(int departmentId, string viewerUserId, string memberUserId);

		/// <summary>Open injuries of the department (health officers), operational columns only.</summary>
		Task<List<RmsMemberInjury>> GetOpenInjuriesAsync(int departmentId, string officerUserId);

		Task<List<RmsInjuryDutyPeriod>> GetDutyPeriodsAsync(int departmentId, string viewerUserId, string injuryId);

		/// <summary>Edits the injury's facts and OSHA classification (health officers).</summary>
		Task<RmsMemberInjury> UpdateInjuryAsync(int departmentId, string officerUserId, string injuryId, MemberInjuryInput input, long? expectedRowVersion, CancellationToken cancellationToken = default);

		/// <summary>Restricted or off duty from a date (health officers): closes the open stretch and opens a new one.</summary>
		Task<RmsMemberInjury> SetDutyStatusAsync(int departmentId, string officerUserId, string injuryId, int dutyStatus, DateTime startsOn, DateTime? expectedReturnOn,
			string restrictions, CancellationToken cancellationToken = default);

		/// <summary>The return-to-duty clearance (health officers): full duty from the date, the injury is cleared.</summary>
		Task<RmsMemberInjury> ClearReturnToDutyAsync(int departmentId, string officerUserId, string injuryId, DateTime returnedOn, string clearanceNotes, CancellationToken cancellationToken = default);

		Task<RmsMemberInjury> VoidInjuryAsync(int departmentId, string officerUserId, string injuryId, CancellationToken cancellationToken = default);

		/// <summary>One member's operational duty and clearance status: the member, a health officer, or their supervisor.</summary>
		Task<MemberDutyStatus> GetDutyStatusAsync(int departmentId, string viewerUserId, string memberUserId);

		/// <summary>
		/// Members not on full duty or not fully cleared, among those the viewer supervises (everyone for a department admin or a health
		/// officer, their group for a group admin, themselves otherwise). Operational only.
		/// </summary>
		Task<List<MemberDutyStatus>> GetDutyBoardAsync(int departmentId, string viewerUserId);

		// ---- OSHA 300 / 301 -------------------------------------------------------------------------------------------------

		/// <summary>The year's OSHA 300 log and 301 reports (health officers), privacy-case names withheld on the log. Audited.</summary>
		Task<OshaLog> BuildOshaLogAsync(int departmentId, string officerUserId, int year, Func<string, string> nameOf);

		// ---- NFPA 1582 medical clearance ------------------------------------------------------------------------------------

		/// <summary>Records an evaluation (health officers): the certification record of the clearance type, when one is named, and the status.</summary>
		Task<RmsMedicalClearance> RecordClearanceAsync(int departmentId, string officerUserId, MedicalClearanceInput input, CancellationToken cancellationToken = default);

		Task<List<RmsMedicalClearance>> GetClearancesForMemberAsync(int departmentId, string viewerUserId, string memberUserId);

		/// <summary>Every member's current clearance (health officers), restriction detail blanked.</summary>
		Task<List<RmsMedicalClearance>> GetCurrentClearancesAsync(int departmentId, string officerUserId);

		Task<RmsMedicalClearance> VoidClearanceAsync(int departmentId, string officerUserId, string clearanceId, CancellationToken cancellationToken = default);

		// ---- GDPR and worker 91 ---------------------------------------------------------------------------------------------

		/// <summary>The member's own cases, injuries and clearances for the unattended GDPR archive (sealed values stay sealed).</summary>
		Task<object> BuildGdprExportAsync(string userId, int departmentId);

		/// <summary>Tells the designated officers about overdue Ryan White answers and the health officers about due return-to-duty reviews.</summary>
		Task<int> SendDueNoticesAsync(DateTime utcNow, CancellationToken cancellationToken = default);

		/// <summary>Erases cases (30-year minimum), injuries (5 years after their year) and clearances past retention that no legal hold covers.</summary>
		Task<int> EraseExpiredAsync(DateTime utcNow, CancellationToken cancellationToken = default);
	}
}
