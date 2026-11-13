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
	/// chat, Incident Command, Incident Back Office, workforce, workflow and Department Import subtrees, then the remaining tables below. Nothing forces their delete,
	/// so they live here rather than in the SQL Server script, and department deletion and the sweep of departments deleted
	/// before their tables were purged (DeletedDepartmentCleanup) run the same code. Not here: the readiness subtree (checklist
	/// cleanup, under its fence), RMS (its own cleanup and orphan sweep) and ADP (deleted last). Retained by design (decided
	/// 2026-10-11): SystemAudits, AdpAuditEvents and ProtectedWorkflowDisclosures, and the session, MFA and transient login
	/// rows the CleanOIDC worker purges on retention. Also retained (2026-11-10): OfflineMapPacks, the only handle to the pack
	/// store files, which the pack retention cleanup deletes together with the row.
	/// </summary>
	public static class UnlinkedDepartmentCleanup
	{
		/// <summary>Every table this cleanup deletes by DepartmentId, module subtrees included.</summary>
		internal static IEnumerable<string> AllTables => ChatDepartmentCleanup.Tables.Concat(IncidentCommandDepartmentCleanup.AllTables).Concat(IncidentBackOfficeDepartmentCleanup.Tables)
			.Concat(WorkforceDepartmentCleanup.Tables).Concat(WorkflowDepartmentCleanup.Tables).Concat(DepartmentImportDepartmentCleanup.Tables).Concat(Tables);

		/// <summary>The tables outside the module subtrees, in delete order.</summary>
		internal static readonly string[] Tables =
		{
			"CallLocationIndexStates", "CallNumberSequences", "DocumentNumberSequences", "DepartmentApiKeys", "ScheduledTasks", "UserStates",
			"UdfDefinitions", "ChatbotSessions", "ChatbotMessageLog", "ChatbotDepartmentConfigs", "AiDispatchAudits", "DepartmentAiDispatchConfigs",
			"AiBillingAccounts", "AdpBillingAccounts", "ContactPreplanHazards", "ContactPreplans", "ContactAttachments", "DepartmentProfileMedia", "TypeIcons", "MapVisualizations", "RoadClosures", "PoiImports", "PoiCapabilityAssignments", "PoiCapabilities", "PoiAvailabilities", "UserDashboards", "DepartmentDashboardTemplates",
			"CallOutstandingRequirements", "RunCardDecisions", "RunCardTargets", "ResponsePackages", "RunCardActivations",
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
			await IncidentBackOfficeDepartmentCleanup.DeleteWithinTransactionAsync(connection, transaction, departmentId, type, ct);
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

			// Chatbot, AI dispatch triage and the AI and ADP add-on billing accounts
			await Delete("ChatbotSessions", "ChatbotMessageLog", "ChatbotDepartmentConfigs", "AiDispatchAudits", "DepartmentAiDispatchConfigs", "AiBillingAccounts", "AdpBillingAccounts");

			// Contact preplans, their hazards and contact attachments; profile media and type icons are keyed by department
			await Delete("ContactPreplanHazards", "ContactPreplans", "ContactAttachments", "DepartmentProfileMedia", "TypeIcons");

			// Saved map visualizations (definitions only; the data they show is never stored)
			await Delete("MapVisualizations");

			// Road closures (M0314): drawn closures with their details and time windows
			await Delete("RoadClosures");

			// Background POI imports (M0313): the upload's pending rows and its report; the POIs themselves go with their types
			await Delete("PoiImports");

			// POI capabilities, their assignments to POIs and destination availability reports (M0315); no foreign keys
			await Delete("PoiCapabilityAssignments", "PoiCapabilities", "PoiAvailabilities");

			// Home dashboard boards and department templates (layouts only)
			await Delete("UserDashboards", "DepartmentDashboardTemplates");

			// The orphan decision-node sweep below reads every department's nodes, so two cleanups in it at once (a deletion and
			// the deleted-department sweep) could each wait on nodes the other just deleted. One transaction-scoped lock, taken
			// before any run-card row is deleted and released when the transaction ends, serializes the run-card section.
			await LockRunCardCleanupAsync(connection, transaction, type, ct);

			// Conditional run cards (M0287): a call's awaited resources and pinned decisions, the "any of" targets of package
			// rows and condition operands, and the response packages with their rows (rows carry no DepartmentId)
			await Delete("CallOutstandingRequirements", "RunCardDecisions", "RunCardTargets");
			await DeleteChild("ResponsePackageRows", false, ("ResponsePackages", "ResponsePackageId", "ResponsePackageId"));
			await Delete("ResponsePackages");

			// Run cards: requirements and decision graph nodes hang off alarm levels, the other children off the run card
			foreach (var levelChild in new[] { "RunCardRoleRequirements", "RunCardUnitRequirements", "RunCardDecisionNodes" })
				if (await DepartmentCleanupCommands.ExistsAsync(connection, transaction, type, levelChild, ct) && await DepartmentCleanupCommands.ExistsAsync(connection, transaction, type, "RunCardAlarmLevels", ct))
					await connection.ExecuteAsync(new CommandDefinition($"DELETE FROM {Q(levelChild)} WHERE {Q("RunCardAlarmLevelId")} IN (SELECT {Q("RunCardAlarmLevelId")} FROM {Q("RunCardAlarmLevels")} WHERE {Q("RunCardId")} IN (SELECT {Q("RunCardId")} FROM {Q("RunCards")} WHERE {Q("DepartmentId")}=@DepartmentId))",
						new { DepartmentId = departmentId }, transaction, cancellationToken: ct));
			foreach (var child in new[] { "RunCardAlarmLevels", "RunCardAvailabilitySelections", "RunCardTriggers" })
				await DeleteChild(child, false, ("RunCards", "RunCardId", "RunCardId"));
			// Decision nodes have no foreign key to their alarm level, and cleanups before 2026-11-11 removed a department's levels without
			// them. Such a node carries no DepartmentId for the deleted-department sweep to find, so nodes whose level is gone go here.
			if (await DepartmentCleanupCommands.ExistsAsync(connection, transaction, type, "RunCardDecisionNodes", ct) && await DepartmentCleanupCommands.ExistsAsync(connection, transaction, type, "RunCardAlarmLevels", ct))
				await connection.ExecuteAsync(new CommandDefinition($"DELETE FROM {Q("RunCardDecisionNodes")} WHERE {Q("RunCardAlarmLevelId")} NOT IN (SELECT {Q("RunCardAlarmLevelId")} FROM {Q("RunCardAlarmLevels")})",
					null, transaction, cancellationToken: ct));
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

		/// <summary>SQL Server application lock resource and PostgreSQL advisory lock key for the run-card section.</summary>
		internal const string RunCardCleanupLockResource = "Resgrid.DepartmentCleanup.RunCards";
		internal const long RunCardCleanupLockKey = 0x5247_5243_4C45_414E; // "RGRCLEAN"
		/// <summary>How long a cleanup waits for another one's run-card section; under the 30-second command timeout.</summary>
		internal const int RunCardCleanupLockTimeoutMs = 20000;
		internal const string RunCardCleanupBusyMessage = "Department deletion is waiting for another department cleanup to finish with run cards.";

		/// <summary>
		/// Takes the run-card section's transaction-scoped lock (released at commit or rollback). A wait past
		/// <see cref="RunCardCleanupLockTimeoutMs"/> defers the deletion like the other deferrals: nothing is spent, a later pass retries.
		/// </summary>
		private static async Task LockRunCardCleanupAsync(DbConnection connection, DbTransaction transaction, DatabaseTypes type, CancellationToken ct)
		{
			if (type == DatabaseTypes.Postgres)
			{
				for (var waitedMs = 0; ; waitedMs += 250)
				{
					if (await connection.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT pg_try_advisory_xact_lock(@Key)", new { Key = RunCardCleanupLockKey }, transaction, cancellationToken: ct)))
						return;
					if (waitedMs >= RunCardCleanupLockTimeoutMs)
						throw new DepartmentDeletionDeferredException(RunCardCleanupBusyMessage);
					await Task.Delay(250, ct);
				}
			}

			// sp_getapplock answers 0 or 1 when granted and a negative code on timeout, cancellation or deadlock.
			var result = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
				"DECLARE @Result int; EXEC @Result = sp_getapplock @Resource = @Resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = @TimeoutMs; SELECT @Result;",
				new { Resource = RunCardCleanupLockResource, TimeoutMs = RunCardCleanupLockTimeoutMs }, transaction, cancellationToken: ct));
			if (result < 0)
				throw new DepartmentDeletionDeferredException(RunCardCleanupBusyMessage);
		}
	}
}
