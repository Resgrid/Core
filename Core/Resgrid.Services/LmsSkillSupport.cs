using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Helpers;
using Resgrid.Model.Lms;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>Setting 146 (<see cref="LmsSkillSettings"/>): how skill notices are delivered and to whom.</summary>
	public class LmsSkillSettingsService : ILmsSkillSettingsService
	{
		private readonly IDepartmentSettingsService _settings;
		private readonly IEventAggregator _events;

		public LmsSkillSettingsService(IDepartmentSettingsService settings, IEventAggregator events)
		{
			_settings = settings;
			_events = events;
		}

		public async Task<LmsSkillSettings> GetAsync(int departmentId)
		{
			if (departmentId <= 0)
				return new LmsSkillSettings().Normalized();
			var setting = await _settings.GetSettingByTypeAsync(departmentId, DepartmentSettingTypes.LmsSkillSettings);
			if (!string.IsNullOrWhiteSpace(setting?.Setting))
			{
				try
				{
					var stored = ObjectSerialization.Deserialize<LmsSkillSettings>(setting.Setting);
					if (stored != null)
						return stored.Normalized();
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Unreadable LMS skill settings for department {departmentId}");
				}
			}
			return new LmsSkillSettings().Normalized();
		}

		public async Task<LmsSkillSettings> SaveAsync(int departmentId, LmsSkillSettings settings, string userId, CancellationToken cancellationToken = default)
		{
			if (departmentId <= 0)
				throw new InvalidOperationException("lms_skill_not_found");
			var stored = (settings ?? new LmsSkillSettings()).Normalized();
			var before = await GetAsync(departmentId);
			await _settings.SaveOrUpdateSettingAsync(departmentId, ObjectSerialization.Serialize(stored), DepartmentSettingTypes.LmsSkillSettings, cancellationToken);
			_events?.SendMessage(new AuditEvent
			{
				DepartmentId = departmentId,
				UserId = userId,
				Type = AuditLogTypes.LmsSkillSettingsChanged,
				Before = JsonConvert.SerializeObject(before),
				After = JsonConvert.SerializeObject(stored),
				Successful = true,
				ServerName = Environment.MachineName
			});
			return stored;
		}
	}

	/// <summary>
	/// The skill notification digest (EventTypes 70 SkillEvaluationNotice and 71 TaskBookNotice, the certification admin-digest pattern):
	/// notices are queued per recipient, due now or at the department's digest time; worker 96 reads only due unsent rows (no department
	/// sweep), claims each before sending, skips inactive members (marked skipped so they are never read again) and sends one push + email
	/// per recipient with every notice due. Admin recipients are the active admins. Outbound text is English like the rest of Resgrid's.
	/// </summary>
	public class LmsSkillNotificationService : ILmsSkillNotificationService
	{
		public const int DueBatch = 500;
		public static readonly TimeSpan ClaimLease = TimeSpan.FromMinutes(10);

		private readonly ILmsSkillNotificationsRepository _notifications;
		private readonly ILmsSkillSettingsService _settings;
		private readonly IDepartmentsService _departments;
		private readonly IDepartmentSettingsService _departmentSettings;
		private readonly Lazy<ICommunicationService> _communication;

		public LmsSkillNotificationService(ILmsSkillNotificationsRepository notifications, ILmsSkillSettingsService settings, IDepartmentsService departments,
			IDepartmentSettingsService departmentSettings, Lazy<ICommunicationService> communication)
		{
			_notifications = notifications;
			_settings = settings;
			_departments = departments;
			_departmentSettings = departmentSettings;
			_communication = communication;
		}

		/// <summary>The push event code the apps route on ("SK:{kind}").</summary>
		public static string EventCode(LmsSkillNotificationKinds kind) => "SK:" + ((int)kind).ToString(CultureInfo.InvariantCulture);

		public static EventTypes EventTypeOf(LmsSkillNotificationKinds kind) =>
			kind == LmsSkillNotificationKinds.EvaluationCompleted || kind == LmsSkillNotificationKinds.EvaluationAwaitingSignature ? EventTypes.SkillEvaluationNotice : EventTypes.TaskBookNotice;

		public async Task<int> QueueAsync(int departmentId, LmsSkillNotificationKinds kind, string text, IEnumerable<string> memberUserIds, bool includeOfficers,
			CancellationToken cancellationToken = default)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(text))
				return 0;
			try
			{
				var settings = await _settings.GetAsync(departmentId);
				if (settings.DigestMode == (int)LmsSkillDigestModes.Off)
					return 0;

				var recipients = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				if (settings.NotifyMembers)
					foreach (var userId in (memberUserIds ?? Enumerable.Empty<string>()).Where(u => !string.IsNullOrWhiteSpace(u)))
						recipients.Add(userId.Trim());
				if (includeOfficers && settings.NotifyOfficers)
					foreach (var admin in await _departments.GetActiveAdminsForDepartmentAsync(departmentId) ?? new List<Model.Identity.IdentityUser>())
						if (!string.IsNullOrWhiteSpace(admin?.UserId))
							recipients.Add(admin.UserId);
				if (recipients.Count == 0)
					return 0;

				var department = await _departments.GetDepartmentByIdAsync(departmentId, false);
				var now = DateTime.UtcNow;
				var due = LmsSkillRules.DueOn(settings, now,
					utc => department == null ? utc : DateTime.SpecifyKind(utc, DateTimeKind.Utc).TimeConverter(department),
					local => department == null ? local : DateTime.SpecifyKind(DateTime.SpecifyKind(local, DateTimeKind.Unspecified).DepartmentLocalToUtc(department), DateTimeKind.Utc));
				var body = text.Length > 1000 ? text.Substring(0, 1000) : text;
				foreach (var userId in recipients)
					await _notifications.InsertAsync(new LmsSkillNotification
					{
						DepartmentId = departmentId,
						UserId = userId,
						Kind = (int)kind,
						Text = body,
						CreatedOn = now,
						DueOn = due
					}, cancellationToken, true);
				return recipients.Count;
			}
			catch (Exception ex)
			{
				// A notice failing never undoes the evaluation or the task book.
				Logging.LogException(ex, $"Skill notices for department {departmentId} could not be queued.");
				return 0;
			}
		}

		public async Task<int> SendDueAsync(DateTime utcNow, CancellationToken cancellationToken = default)
		{
			var due = await _notifications.GetDueAsync(utcNow, utcNow - ClaimLease, DueBatch) ?? new List<LmsSkillNotification>();
			if (due.Count == 0)
				return 0;

			var sent = 0;
			foreach (var department in due.GroupBy(n => n.DepartmentId))
			{
				cancellationToken.ThrowIfCancellationRequested();
				try
				{
					// Inactive-member policy: deleted, disabled and hidden members are never addressed by automation.
					var active = new HashSet<string>(await _departments.GetActiveMemberUserIdsAsync(department.Key) ?? new HashSet<string>(), StringComparer.OrdinalIgnoreCase);
					var dept = await _departments.GetDepartmentByIdAsync(department.Key, false);
					var number = dept == null ? null : await _departmentSettings.GetTextToCallNumberForDepartmentAsync(department.Key);
					foreach (var recipient in department.GroupBy(n => n.UserId, StringComparer.OrdinalIgnoreCase))
					{
						var claimed = new List<LmsSkillNotification>();
						foreach (var notice in recipient)
							if (await _notifications.ClaimAsync(notice.LmsSkillNotificationId, utcNow, utcNow - ClaimLease, cancellationToken))
								claimed.Add(notice);
						if (claimed.Count == 0)
							continue;

						if (dept == null || !active.Contains(recipient.Key) || !ConfigHelper.CanTransmit(department.Key))
						{
							await _notifications.MarkSentAsync(claimed.Select(n => n.LmsSkillNotificationId), utcNow, true, cancellationToken);
							continue;
						}

						var ordered = claimed.OrderBy(n => n.CreatedOn).ThenBy(n => n.LmsSkillNotificationId).ToList();
						var message = ordered.Count == 1 ? ordered[0].Text
							: "Training skills digest: " + string.Join(" | ", ordered.Take(25).Select(n => n.Text)) + (ordered.Count > 25 ? " | +" + (ordered.Count - 25).ToString(CultureInfo.InvariantCulture) + " more" : string.Empty);
						var kind = (LmsSkillNotificationKinds)ordered[0].Kind;
						try
						{
							await _communication.Value.SendNotificationAsync(recipient.Key, department.Key, message, number, dept, ordered.Count == 1 ? "Training skills" : "Training skills digest",
								null, false, EventCode(kind));
							sent++;
						}
						catch (Exception ex)
						{
							Logging.LogException(ex, $"Skill notice to {recipient.Key} in department {department.Key} failed.");
						}
						await _notifications.MarkSentAsync(ordered.Select(n => n.LmsSkillNotificationId), utcNow, false, cancellationToken);
					}
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Skill notices for department {department.Key} could not be sent.");
				}
			}

			return sent;
		}
	}

	/// <summary>
	/// Skill sheets and task-book tasks as 4C competency subjects: a sheet's id, or "{definitionId}:{taskKey}" for a task; evidence is the
	/// member's passed, completed evaluations (repositories only, so the competency service can depend on it without a cycle).
	/// </summary>
	public class LmsSkillCompetencySubjects : ILmsSkillCompetencySubjects
	{
		private readonly ILmsSkillSheetsRepository _sheets;
		private readonly ILmsTaskBookDefinitionsRepository _definitions;
		private readonly ILmsTaskBooksRepository _books;
		private readonly ILmsSkillEvaluationsRepository _evaluations;

		public LmsSkillCompetencySubjects(ILmsSkillSheetsRepository sheets, ILmsTaskBookDefinitionsRepository definitions, ILmsTaskBooksRepository books,
			ILmsSkillEvaluationsRepository evaluations)
		{
			_sheets = sheets;
			_definitions = definitions;
			_books = books;
			_evaluations = evaluations;
		}

		public static string TaskSubjectId(int definitionId, string taskKey) => LmsSkillRules.TaskSubjectId(definitionId, taskKey);

		public async Task<bool> ExistsAsync(int departmentId, LmsCompetencySubjectKinds kind, string subjectId)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(subjectId))
				return false;
			if (kind == LmsCompetencySubjectKinds.SkillSheet)
			{
				if (!int.TryParse(subjectId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id <= 0)
					return false;
				var sheet = await _sheets.GetByIdForDepartmentAsync(departmentId, id);
				return sheet != null && sheet.DepartmentId == departmentId && !sheet.IsDeleted;
			}
			if (kind == LmsCompetencySubjectKinds.TaskBookTask)
			{
				var parts = subjectId.Split(':');
				if (parts.Length != 2 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var definitionId) || definitionId <= 0)
					return false;
				var definition = await _definitions.GetByIdForDepartmentAsync(departmentId, definitionId);
				return definition != null && definition.DepartmentId == departmentId && !definition.IsDeleted &&
					LmsSkillRules.ParseTasks(definition.TasksJson).Any(t => string.Equals(t.Key, parts[1], StringComparison.OrdinalIgnoreCase));
			}
			return false;
		}

		public async Task<List<LmsCompetencyEvidence>> GetEvidenceAsync(int departmentId, string userId)
		{
			var evidence = new List<LmsCompetencyEvidence>();
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId))
				return evidence;
			var passed = (await _evaluations.GetForMemberAsync(departmentId, userId, null) ?? new List<LmsSkillEvaluation>())
				.Where(e => e.DepartmentId == departmentId && e.Status == (int)LmsSkillEvaluationStatuses.Completed && e.Result == (int)LmsSkillEvaluationResults.Passed).ToList();
			if (passed.Count == 0)
				return evidence;

			foreach (var evaluation in passed.Where(e => e.LmsSkillSheetId.HasValue))
				evidence.Add(new LmsCompetencyEvidence
				{
					Kind = LmsCompetencySubjectKinds.SkillSheet,
					SubjectId = evaluation.LmsSkillSheetId.Value.ToString(CultureInfo.InvariantCulture),
					On = evaluation.CompletedOn ?? evaluation.SubmittedOn ?? evaluation.StartedOn,
					Title = evaluation.Title
				});

			var bookIds = passed.Where(e => e.LmsTaskBookId.HasValue && !string.IsNullOrWhiteSpace(e.TaskKey)).Select(e => e.LmsTaskBookId.Value).Distinct().ToList();
			if (bookIds.Count > 0)
			{
				var books = (await _books.GetForMemberAsync(departmentId, userId) ?? new List<LmsTaskBook>()).Where(b => bookIds.Contains(b.LmsTaskBookId)).ToDictionary(b => b.LmsTaskBookId);
				foreach (var evaluation in passed.Where(e => e.LmsTaskBookId.HasValue && !string.IsNullOrWhiteSpace(e.TaskKey)))
					if (books.TryGetValue(evaluation.LmsTaskBookId.Value, out var book))
						evidence.Add(new LmsCompetencyEvidence
						{
							Kind = LmsCompetencySubjectKinds.TaskBookTask,
							SubjectId = TaskSubjectId(book.LmsTaskBookDefinitionId, evaluation.TaskKey),
							On = evaluation.CompletedOn ?? evaluation.SubmittedOn ?? evaluation.StartedOn,
							Title = evaluation.Title
						});
			}

			return evidence;
		}
	}
}
