using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using CommandDefinition = Dapper.CommandDefinition;
using Resgrid.Config;
using Resgrid.Model;

namespace Resgrid.Repositories.DataRepository
{
	/// <summary>
	/// Department tables without a foreign key to Departments, in the caller's transaction, after the legal hold checks: the
	/// chat, Incident Command, workforce, workflow and Department Import subtrees, then the remaining tables below. Nothing forces their delete,
	/// so they live here rather than in the SQL Server script, and department deletion and the sweep of departments deleted
	/// before their tables were purged (DeletedDepartmentCleanup) run the same code. Not here: the readiness subtree (checklist
	/// cleanup, under its fence), RMS (its own cleanup and orphan sweep) and ADP (deleted last). Retained by design (decided
	/// 2026-10-11): SystemAudits, AdpAuditEvents and ProtectedWorkflowDisclosures, and the session, MFA and transient login
	/// rows the CleanOIDC worker purges on retention.
	/// </summary>
	public static class UnlinkedDepartmentCleanup
	{
		/// <summary>Every table this cleanup deletes by DepartmentId, module subtrees included.</summary>
		internal static IEnumerable<string> AllTables => ChatDepartmentCleanup.Tables.Concat(IncidentCommandDepartmentCleanup.AllTables)
			.Concat(WorkforceDepartmentCleanup.Tables).Concat(WorkflowDepartmentCleanup.Tables).Concat(DepartmentImportDepartmentCleanup.Tables).Concat(Tables);

		/// <summary>The tables outside the module subtrees, in delete order.</summary>
		internal static readonly string[] Tables =
		{
			"CallLocationIndexStates", "CallNumberSequences", "DocumentNumberSequences", "DepartmentApiKeys", "ScheduledTasks", "UserStates",
			"UdfDefinitions", "ChatbotSessions", "ChatbotMessageLog", "ChatbotDepartmentConfigs", "AiDispatchAudits", "DepartmentAiDispatchConfigs",
			"AiBillingAccounts", "ContactPreplanHazards", "ContactPreplans", "ContactAttachments", "DepartmentProfileMedia", "TypeIcons", "RunCardActivations",
			"RunCards", "StationCoverageRequirements", "ModerationActions", "ModerationReports", "ModerationRequests", "MessageRecipients", "Messages",
			"DepartmentMemberSensitiveData", "DepartmentMemberEmergencyContacts", "UserExternalIdentityLinks", "DepartmentSecurityPolicies",
			"DepartmentSsoConfigs", "GdprDataExportRequests", "Autofills", "DomainEventOutbox", "ReportingDailyRollup", "SearchProjections",
			"VoiceTransmissionLogs"
		};

		public static async Task DeleteWithinTransactionAsync(DbConnection connection, DbTransaction transaction, int departmentId, DatabaseTypes type, CancellationToken ct = default)
		{
			DepartmentCleanupCommands.RequireTransaction(connection, transaction, departmentId, "Unlinked");
			string Q(string value) => DepartmentCleanupCommands.Q(type, value);
			Task Delete(params string[] tables) => DepartmentCleanupCommands.DeleteByDepartmentAsync(connection, transaction, type, departmentId, tables, ct);
			Task DeleteChild(string child, bool hasDepartment, params (string Parent, string Key, string ChildColumn)[] parents) =>
				DepartmentCleanupCommands.DeleteChildAsync(connection, transaction, type, departmentId, child, hasDepartment, ct, parents);

			// Module subtrees. The workflow cleanup follows the checklist, inventory and RMS cleanups, which remove their own runs first
			await ChatDepartmentCleanup.DeleteWithinTransactionAsync(connection, transaction, departmentId, type, ct);
			await IncidentCommandDepartmentCleanup.DeleteWithinTransactionAsync(connection, transaction, departmentId, type, ct);
			await WorkforceDepartmentCleanup.DeleteWithinTransactionAsync(connection, transaction, departmentId, type, ct);
			await WorkflowDepartmentCleanup.DeleteWithinTransactionAsync(connection, transaction, departmentId, type, ct);
			await DepartmentImportDepartmentCleanup.DeleteWithinTransactionAsync(connection, transaction, departmentId, type, ct);

			// Counters, index state and API keys
			await Delete("CallLocationIndexStates", "CallNumberSequences", "DocumentNumberSequences", "DepartmentApiKeys");

			// Per-member rows swept department-wide, including members who left before the deletion. Task logs reference the task
			await DeleteChild("ScheduledTaskLogs", false, ("ScheduledTasks", "ScheduledTaskId", "ScheduledTaskId"));
			await Delete("ScheduledTasks", "UserStates");

			// User-defined fields (fields and values hang off the definition)
			await DeleteChild("UdfFieldValues", false, ("UdfDefinitions", "UdfDefinitionId", "UdfDefinitionId"));
			await DeleteChild("UdfFields", false, ("UdfDefinitions", "UdfDefinitionId", "UdfDefinitionId"));
			await Delete("UdfDefinitions");

			// Chatbot, AI dispatch triage and the AI add-on billing account
			await Delete("ChatbotSessions", "ChatbotMessageLog", "ChatbotDepartmentConfigs", "AiDispatchAudits", "DepartmentAiDispatchConfigs", "AiBillingAccounts");

			// Contact preplans, their hazards and contact attachments; profile media and type icons are keyed by department
			await Delete("ContactPreplanHazards", "ContactPreplans", "ContactAttachments", "DepartmentProfileMedia", "TypeIcons");

			// Run cards: requirements hang off alarm levels, the other children off the run card
			foreach (var requirement in new[] { "RunCardRoleRequirements", "RunCardUnitRequirements" })
				if (await DepartmentCleanupCommands.ExistsAsync(connection, transaction, type, requirement, ct) && await DepartmentCleanupCommands.ExistsAsync(connection, transaction, type, "RunCardAlarmLevels", ct))
					await connection.ExecuteAsync(new CommandDefinition($"DELETE FROM {Q(requirement)} WHERE {Q("RunCardAlarmLevelId")} IN (SELECT {Q("RunCardAlarmLevelId")} FROM {Q("RunCardAlarmLevels")} WHERE {Q("RunCardId")} IN (SELECT {Q("RunCardId")} FROM {Q("RunCards")} WHERE {Q("DepartmentId")}=@DepartmentId))",
						new { DepartmentId = departmentId }, transaction, cancellationToken: ct));
			foreach (var child in new[] { "RunCardAlarmLevels", "RunCardAvailabilitySelections", "RunCardTriggers" })
				await DeleteChild(child, false, ("RunCards", "RunCardId", "RunCardId"));
			await Delete("RunCardActivations", "RunCards", "StationCoverageRequirements");

			// Moderation (department admins moderate): reports and actions reference the request
			await DeleteChild("ModerationActions", true, ("ModerationRequests", "ModerationRequestId", "ModerationRequestId"));
			await DeleteChild("ModerationReports", true, ("ModerationRequests", "ModerationRequestId", "ModerationRequestId"));
			await Delete("ModerationRequests");

			// Department messages, including those of members who left (the script's member cursors cover current members).
			// Recipients and attached files reference the message
			await DeleteChild("MessageRecipients", true, ("Messages", "MessageId", "MessageId"), ("Messages", "MessageId", "Message_MessageId"));
			await DeleteChild("Files", false, ("Messages", "MessageId", "MessageId"));
			await Delete("Messages");

			// Department-scoped personal data of members, including members who left (ADP plan 5.1), SSO links and security config
			await Delete("DepartmentMemberSensitiveData", "DepartmentMemberEmergencyContacts", "UserExternalIdentityLinks", "DepartmentSecurityPolicies",
				"DepartmentSsoConfigs", "GdprDataExportRequests");

			// Sessions are the sign-in access record: revoke the department's live ones and leave them to the CleanOIDC retention purge
			if (await DepartmentCleanupCommands.ExistsAsync(connection, transaction, type, "UserSessions", ct))
				await connection.ExecuteAsync(new CommandDefinition(
					$"UPDATE {Q("UserSessions")} SET {Q("State")}=@RevokedState, {Q("StateVersion")}={Q("StateVersion")}+1, {Q("RevokedOn")}=@RevokedOn, {Q("RevokedByUserId")}=NULL, {Q("RevocationReason")}=@Reason WHERE {Q("DepartmentId")}=@DepartmentId AND {Q("State")}=@ActiveState",
					new
					{
						DepartmentId = departmentId,
						ActiveState = (int)UserSessionState.Active,
						RevokedState = (int)UserSessionState.Revoked,
						Reason = (int)UserSessionRevocationReason.DepartmentDeleted,
						RevokedOn = type == DatabaseTypes.Postgres ? DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified) : DateTime.UtcNow
					}, transaction, cancellationToken: ct));

			// Every producer's outbox rows (the module cleanups removed their own already), rollups, and unified search
			// projections. SearchIndexStates stays as the marker the global index sweep uses to erase the department's documents
			await Delete("Autofills", "DomainEventOutbox", "ReportingDailyRollup", "SearchProjections", "VoiceTransmissionLogs");
		}
	}
}
