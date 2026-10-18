using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Resources;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Localization;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Identity;
using Resgrid.Model.Onboarding;
using Resgrid.Model.Providers;
using Resgrid.Model.Queue;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Onboarding
{
	/// <summary>
	/// Workspace-owner notices for Department Import (EventTypes 50 and 51). Messages carry the outcome, an issue code
	/// and a link, never a value from the import (conformance I4). They go to the workspace owner while the owner is still
	/// an active department administrator, otherwise to the first active administrator; dormant people are never active
	/// members, so an imported person is never the target. Each notice is written in the recipient's language (their
	/// profile's, English when unset or unsupported), so administrators of one department can each read their own.
	/// </summary>
	public class DepartmentImportNotificationService : IDepartmentImportNotificationService
	{
		private static readonly ResourceManager Strings =
			new ResourceManager("Resgrid.Localization.Areas.User.DepartmentImport.DepartmentImport", typeof(SupportedLocales).Assembly);
		private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en");

		private readonly IOutboundQueueProvider _outboundQueue;
		private readonly IImportRunsRepository _runs;
		private readonly IImportWorkspacesRepository _workspaces;
		private readonly IDepartmentsService _departments;
		private readonly IDepartmentSettingsService _departmentSettings;
		private readonly IUserProfileService _profiles;
		private readonly ICommunicationService _communication;

		public DepartmentImportNotificationService(IOutboundQueueProvider outboundQueue, IImportRunsRepository runs, IImportWorkspacesRepository workspaces,
			IDepartmentsService departments, IDepartmentSettingsService departmentSettings, IUserProfileService profiles, ICommunicationService communication)
		{
			_outboundQueue = outboundQueue;
			_runs = runs;
			_workspaces = workspaces;
			_departments = departments;
			_departmentSettings = departmentSettings;
			_profiles = profiles;
			_communication = communication;
		}

		public Task NotifyRunFinishedAsync(ImportRun run, CancellationToken cancellationToken = default) =>
			EnqueueAsync(run.DepartmentId, EventTypes.DepartmentImportRunFinished, "run:" + run.Id);

		public Task NotifyAbandonmentReminderAsync(ImportWorkspace workspace, CancellationToken cancellationToken = default) =>
			EnqueueAsync(workspace.DepartmentId, EventTypes.DepartmentImportRunFinished, "reminder:" + workspace.Id);

		public Task NotifySupportActivityAsync(int departmentId, string workspaceId, string activity, CancellationToken cancellationToken = default) =>
			EnqueueAsync(departmentId, EventTypes.DepartmentImportSupportMessage, $"{workspaceId}|{activity}");

		public Task NotifyAdministratorsAsync(int departmentId, string workspaceId, string activity, CancellationToken cancellationToken = default) =>
			EnqueueAsync(departmentId, EventTypes.DepartmentImportSupportMessage, $"{workspaceId}|{activity}|all");

		public async Task<bool> DeliverRunNoticeAsync(int departmentId, string value, CancellationToken cancellationToken = default)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(value))
				return false;

			if (value.StartsWith("reminder:", StringComparison.Ordinal))
			{
				var idle = await _workspaces.GetByIdAsync(departmentId, value.Substring(9), cancellationToken);
				if (idle == null || DepartmentImportStateMachine.IsTerminal((ImportWorkspaceStatus)idle.Status))
					return false;

				return await SendAsync(departmentId, idle.OwnerUserId, "ModuleName", culture => BuildReminderMessage(idle, culture), cancellationToken);
			}

			if (!value.StartsWith("run:", StringComparison.Ordinal))
				return false;

			var run = await _runs.GetByIdAsync(departmentId, value.Substring(4), cancellationToken);
			if (run == null)
				return false;

			var workspace = await _workspaces.GetByIdAsync(departmentId, run.WorkspaceId, cancellationToken);
			if (workspace == null)
				return false;

			return await SendAsync(departmentId, workspace.OwnerUserId, "ModuleName", culture => BuildRunMessage(run, workspace, culture), cancellationToken);
		}

		public async Task<bool> DeliverSupportNoticeAsync(int departmentId, string value, CancellationToken cancellationToken = default)
		{
			var parts = (value ?? string.Empty).Split('|');
			if (departmentId <= 0 || parts.Length < 2)
				return false;

			var workspace = await _workspaces.GetByIdAsync(departmentId, parts[0], cancellationToken);
			if (workspace == null)
				return false;

			string Message(CultureInfo culture) => BuildSupportMessage(workspace, parts[1], culture);
			if (parts.Length < 3 || parts[2] != "all")
				return await SendAsync(departmentId, workspace.OwnerUserId, "Notify.SupportTitle", Message, cancellationToken);

			// Every active administrator: nobody in the department is surprised by staff access or by a send (section 17.8).
			var sent = false;
			foreach (var admin in (await _departments.GetActiveAdminsForDepartmentAsync(departmentId)) ?? new List<IdentityUser>())
				if (!string.IsNullOrWhiteSpace(admin.Id))
					sent |= await SendToAsync(departmentId, admin.Id, "Notify.SupportTitle", Message, cancellationToken);
			return sent;
		}

		/// <summary>The recipient's language when Resgrid supports it, otherwise English.</summary>
		public static CultureInfo CultureFor(string language)
		{
			try
			{
				var culture = CultureInfo.GetCultureInfo(string.IsNullOrWhiteSpace(language) ? "en" : language);
				if (SupportedLocales.GetSupportedCultures().Contains(culture.TwoLetterISOLanguageName))
					return culture;
			}
			catch (CultureNotFoundException)
			{
			}

			return English;
		}

		/// <summary>A notice string in <paramref name="culture"/>, falling back to English (the en satellite holds every key).</summary>
		public static string Text(string key, CultureInfo culture) =>
			Strings.GetString(key, culture ?? English) ?? Strings.GetString(key, English) ?? key;

		private static string Format(CultureInfo culture, string key, params object[] args) =>
			string.Format(culture ?? English, Text(key, culture), args);

		/// <summary>"The import in "Spring roster" has finished (code …). Open Import &amp; Migration: link", in <paramref name="culture"/>.</summary>
		public static string BuildRunMessage(ImportRun run, ImportWorkspace workspace, CultureInfo culture = null)
		{
			var kind = ((ImportRunKind)run.Kind) switch
			{
				ImportRunKind.Commit => "Commit",
				ImportRunKind.Rollback => "Rollback",
				ImportRunKind.Reconcile => "Reconcile",
				ImportRunKind.Extract => "Extract",
				ImportRunKind.Activation => "Activation",
				_ => "Analysis"
			};

			var outcome = ((ImportRunStatus)run.Status) switch
			{
				ImportRunStatus.Completed => "Completed",
				ImportRunStatus.Paused => "Paused",
				ImportRunStatus.Failed => "Failed",
				ImportRunStatus.Cancelled => "Cancelled",
				_ => "Other"
			};

			// Each kind has its own sentence per outcome: the noun's gender decides the verb's form in most languages.
			var code = string.IsNullOrWhiteSpace(run.LastErrorCode) ? string.Empty : Format(culture, "Notify.Run.Code", run.LastErrorCode);
			return Format(culture, $"Notify.Run.{kind}.{outcome}", Truncate(workspace.Name, 80), code, Link(workspace.Id));
		}

		public static string BuildReminderMessage(ImportWorkspace workspace, CultureInfo culture = null) =>
			Format(culture, "Notify.Reminder", Truncate(workspace.Name, 80), Link(workspace.Id));

		public static string BuildSupportMessage(ImportWorkspace workspace, string activity, CultureInfo culture = null)
		{
			var name = Truncate(workspace.Name, 80);
			// "managed-invitations;{people};{notBefore ISO}" and "managed-activation;{people};{notBefore ISO}" (section 17.8).
			var managed = (activity ?? string.Empty).Split(';');
			if (managed.Length == 3 && (managed[0] == "managed-invitations" || managed[0] == "managed-activation"))
			{
				var when = DateTime.TryParse(managed[2], CultureInfo.InvariantCulture,
					DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var notBefore)
					? Format(culture, "Notify.Managed.On", notBefore.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))
					: Text("Notify.Managed.OnceApproved", culture);
				return Format(culture, managed[0] == "managed-invitations" ? "Notify.Managed.Invitations" : "Notify.Managed.Activation",
					name, when, VerifyLink(workspace.Id), managed[1]);
			}

			// "staff-views;{rows};{staff}": the daily digest of staff row views (section 17.2).
			if (managed.Length == 3 && managed[0] == "staff-views")
				return Format(culture, "Notify.StaffViews", name, Link(workspace.Id), managed[1], managed[2]);

			var key = activity switch
			{
				"contract-authorization" => "Notify.ContractAuthorization",
				"artifact-downloaded" => "Notify.ArtifactDownloaded",
				"sample-viewed" => "Notify.SampleViewed",
				"signoff-needed" => "Notify.SignoffNeeded",
				"managed-run" => "Notify.ManagedRun",
				"suggestion" => "Notify.Suggestion",
				"status" => "Notify.Status",
				"grant" => "Notify.Grant",
				_ => "Notify.Message"
			};
			return Format(culture, key, name, Link(workspace.Id));
		}

		private static string Link(string workspaceId) =>
			$"{Config.SystemBehaviorConfig.ResgridBaseUrl}/User/DepartmentImport/Workspace/{workspaceId}";

		private static string VerifyLink(string workspaceId) =>
			$"{Config.SystemBehaviorConfig.ResgridBaseUrl}/User/DepartmentImport/Verify/{workspaceId}";

		private async Task EnqueueAsync(int departmentId, EventTypes type, string value)
		{
			try
			{
				if (!await _outboundQueue.EnqueueNotification(new NotificationItem { DepartmentId = departmentId, Type = (int)type, Value = value }))
					Logging.LogError($"Department import notice {type} for department {departmentId} was not enqueued.");
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Department import notice {type} for department {departmentId} was not enqueued.");
			}
		}

		private async Task<bool> SendAsync(int departmentId, string ownerUserId, string titleKey, Func<CultureInfo, string> message, CancellationToken cancellationToken)
		{
			var target = await TargetAsync(departmentId, ownerUserId);
			if (target == null)
				return false;

			return await SendToAsync(departmentId, target, titleKey, message, cancellationToken);
		}

		/// <summary>Writes the notice in the recipient's language, then sends it.</summary>
		private async Task<bool> SendToAsync(int departmentId, string target, string titleKey, Func<CultureInfo, string> message, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();

			try
			{
				var department = await _departments.GetDepartmentByIdAsync(departmentId, false);
				var departmentNumber = await _departmentSettings.GetTextToCallNumberForDepartmentAsync(departmentId);
				var profile = await _profiles.GetProfileByUserIdAsync(target, false);
				var culture = CultureFor(profile?.Language);
				return await _communication.SendNotificationAsync(target, departmentId, message(culture), departmentNumber, department, Text(titleKey, culture), profile);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Department import notice for department {departmentId} could not be sent.");
				return false;
			}
		}

		/// <summary>The owner while still an active administrator; otherwise the first active administrator; null when none.</summary>
		private async Task<string> TargetAsync(int departmentId, string ownerUserId)
		{
			var admins = await _departments.GetActiveAdminsForDepartmentAsync(departmentId);
			if (admins == null || admins.Count == 0)
				return null;

			if (!string.IsNullOrWhiteSpace(ownerUserId) && admins.Any(a => string.Equals(a.Id, ownerUserId, StringComparison.OrdinalIgnoreCase)))
				return ownerUserId;

			return admins.Select(a => a.Id).FirstOrDefault(id => !string.IsNullOrWhiteSpace(id));
		}

		private static string Truncate(string value, int length) =>
			string.IsNullOrEmpty(value) ? "Import" : value.Length <= length ? value : value.Substring(0, length);
	}
}
