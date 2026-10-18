using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Onboarding;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Onboarding
{
	/// <summary>
	/// The administrator's side of Import Support (wizard plan sections 17.2 and 17.5). Grants are versioned and revocable;
	/// Operate exists only when the operator-only flag Onboarding.ManagedMigration is on. Support requests are saved in-app
	/// at once and mirrored to the helpdesk through the outbox, carrying metadata only (conformance I15).
	/// </summary>
	public class DepartmentImportSupportService : IDepartmentImportSupportService, IDepartmentImportSupportOutbox
	{
		private const int MaxMessageLength = 4000;

		private readonly IImportSupportGrantsRepository _grants;
		private readonly IImportSupportRequestsRepository _requests;
		private readonly IImportMessagesRepository _messages;
		private readonly IImportSuggestionsRepository _suggestions;
		private readonly IImportWorkspacesRepository _workspaces;
		private readonly IImportIssuesRepository _issues;
		private readonly IDepartmentImportAccessService _access;
		private readonly IImportTimelineService _timeline;
		private readonly IImportMappingService _mapping;
		private readonly IImportIssueService _issueService;
		private readonly IHelpdeskProvider _helpdesk;
		private readonly IDepartmentsService _departments;
		private readonly IUsersService _users;
		private readonly IUserProfileService _profiles;
		private readonly IEventAggregator _events;

		public DepartmentImportSupportService(IImportSupportGrantsRepository grants, IImportSupportRequestsRepository requests, IImportMessagesRepository messages,
			IImportSuggestionsRepository suggestions, IImportWorkspacesRepository workspaces, IImportIssuesRepository issues, IDepartmentImportAccessService access,
			IImportTimelineService timeline, IImportMappingService mapping, IImportIssueService issueService, IHelpdeskProvider helpdesk, IDepartmentsService departments,
			IUsersService users, IUserProfileService profiles, IEventAggregator events)
		{
			_grants = grants;
			_requests = requests;
			_messages = messages;
			_suggestions = suggestions;
			_workspaces = workspaces;
			_issues = issues;
			_access = access;
			_timeline = timeline;
			_mapping = mapping;
			_issueService = issueService;
			_helpdesk = helpdesk;
			_departments = departments;
			_users = users;
			_profiles = profiles;
			_events = events;
		}

		public Task<List<ImportSupportGrant>> GetGrantsAsync(int departmentId, string workspaceId, CancellationToken cancellationToken = default) =>
			_grants.GetForWorkspaceAsync(departmentId, workspaceId, cancellationToken);

		public async Task<ImportSupportGrant> GetActiveGrantAsync(int departmentId, string workspaceId, CancellationToken cancellationToken = default) =>
			ActiveGrant(await _grants.GetForWorkspaceAsync(departmentId, workspaceId, cancellationToken));

		public static ImportSupportGrant ActiveGrant(IEnumerable<ImportSupportGrant> grants) =>
			grants.Where(g => g.Status == (int)ImportGrantStatus.Active && g.ExpiresOn > DateTime.UtcNow && !g.RevokedOn.HasValue)
				.OrderByDescending(g => g.GrantedOn).FirstOrDefault();

		public async Task<ImportSupportGrant> GrantAccessAsync(int departmentId, string userId, string workspaceId, ImportSupportAccessLevel level, int days,
			ImportOperateActions allowedActions, bool requireDepartmentSignOff, string message, string ipAddress, CancellationToken cancellationToken = default)
		{
			var workspace = await _workspaces.GetByIdAsync(departmentId, workspaceId, cancellationToken) ?? throw new DepartmentImportException("import_workspace_not_found", 404);
			if (!Enum.IsDefined(typeof(ImportSupportAccessLevel), level) || level == ImportSupportAccessLevel.None)
				throw new DepartmentImportException("import_grant_level_invalid");

			// Operate is offered only for an agreed engagement (the operator-only flag); it is unadvertised otherwise.
			if (level == ImportSupportAccessLevel.Operate && !await _access.IsManagedMigrationEnabledAsync(departmentId))
				throw new DepartmentImportException("import_grant_level_invalid");

			var maxDays = level == ImportSupportAccessLevel.Operate ? DepartmentImportConfig.MaxOperateGrantDays : DepartmentImportConfig.MaxSupportGrantDays;
			days = days <= 0 ? DepartmentImportConfig.DefaultSupportGrantDays : Math.Min(days, maxDays);

			var now = DateTime.UtcNow;
			var existing = await _grants.GetForWorkspaceAsync(departmentId, workspaceId, cancellationToken);
			foreach (var previous in existing.Where(g => g.Status == (int)ImportGrantStatus.Active))
			{
				previous.Status = (int)ImportGrantStatus.Revoked;
				previous.RevokedBy = userId;
				previous.RevokedOn = now;
				previous.Version++;
				await _grants.UpdateAsync(previous, cancellationToken, true);
			}

			var grant = new ImportSupportGrant
			{
				DepartmentId = departmentId,
				WorkspaceId = workspaceId,
				Level = (int)level,
				AllowedActions = level == ImportSupportAccessLevel.Operate ? (int)(allowedActions | ImportOperateActions.Prepare) : 0,
				AuthorizationKind = (int)ImportGrantAuthorizationKind.DepartmentAdministrator,
				Status = (int)ImportGrantStatus.Active,
				GrantedByUserId = userId,
				OnBehalfOfUserId = level == ImportSupportAccessLevel.Operate ? userId : null,
				RequireDepartmentSignOff = level == ImportSupportAccessLevel.Operate && requireDepartmentSignOff,
				Message = Clip(message, 2000),
				GrantedOn = now,
				ExpiresOn = now.AddDays(days),
				Version = (existing.Max(g => (int?)g.Version) ?? 0) + 1
			};
			await _grants.InsertAsync(grant, cancellationToken, true);

			workspace.SupportAccessLevel = grant.Level;
			workspace.SupportAccessExpiresOn = grant.ExpiresOn;
			if (level == ImportSupportAccessLevel.Operate)
				workspace.OperatingModel = (int)ImportOperatingModel.ManagedMigration;
			workspace.UpdatedOn = now;
			await _workspaces.UpdateWithRevisionAsync(workspace, workspace.Revision, cancellationToken);

			await _timeline.RecordAsync(departmentId, workspaceId, ImportActorKind.Administrator, userId, "SupportAccessGranted",
				new { level = level.ToString(), days, allowed = grant.AllowedActions }, cancellationToken: cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.DepartmentImportSupportAccessGranted, new { workspaceId, level = level.ToString(), days }, ipAddress);
			return grant;
		}

		public async Task RevokeAccessAsync(int departmentId, string userId, string workspaceId, string ipAddress, CancellationToken cancellationToken = default)
		{
			var workspace = await _workspaces.GetByIdAsync(departmentId, workspaceId, cancellationToken) ?? throw new DepartmentImportException("import_workspace_not_found", 404);
			var now = DateTime.UtcNow;
			var revoked = 0;
			foreach (var grant in (await _grants.GetForWorkspaceAsync(departmentId, workspaceId, cancellationToken))
				.Where(g => g.Status == (int)ImportGrantStatus.Active || g.Status == (int)ImportGrantStatus.PendingApproval))
			{
				// Revocation is immediate for reads and stops staff work at the next chunk boundary (the per-chunk grant check).
				grant.Status = (int)ImportGrantStatus.Revoked;
				grant.RevokedBy = userId;
				grant.RevokedOn = now;
				grant.Version++;
				await _grants.UpdateAsync(grant, cancellationToken, true);
				revoked++;
			}

			workspace.SupportAccessLevel = (int)ImportSupportAccessLevel.None;
			workspace.SupportAccessExpiresOn = null;
			workspace.OperatingModel = (int)ImportOperatingModel.SelfService;
			workspace.UpdatedOn = now;
			await _workspaces.UpdateWithRevisionAsync(workspace, workspace.Revision, cancellationToken);

			await _timeline.RecordAsync(departmentId, workspaceId, ImportActorKind.Administrator, userId, "SupportAccessRevoked", new { grants = revoked },
				cancellationToken: cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.DepartmentImportSupportAccessRevoked, new { workspaceId, grants = revoked }, ipAddress);
		}

		public async Task<ImportSupportRequest> OpenRequestAsync(int departmentId, string userId, string workspaceId, ImportSupportRequestCategory category, string subject,
			string description, string datasetId, string runId, IReadOnlyCollection<string> issueCodes, CancellationToken cancellationToken = default)
		{
			var workspace = await _workspaces.GetByIdAsync(departmentId, workspaceId, cancellationToken) ?? throw new DepartmentImportException("import_workspace_not_found", 404);
			if (!Enum.IsDefined(typeof(ImportSupportRequestCategory), category))
				throw new DepartmentImportException("import_request_category_invalid");
			if (category == ImportSupportRequestCategory.RequestManagedMigration && !await _access.IsManagedMigrationEnabledAsync(departmentId))
				throw new DepartmentImportException("import_request_category_invalid");
			if (string.IsNullOrWhiteSpace(subject))
				throw new DepartmentImportException("import_request_subject_required");

			// Context chips: codes and counts only, never values (section 17.5).
			var openIssues = (await _issues.GetAllForWorkspaceAsync(departmentId, workspaceId, cancellationToken)).Where(i => i.Status == (int)ImportIssueStatus.Open).ToList();
			var codes = (issueCodes ?? Array.Empty<string>()).Where(c => c != null && c.StartsWith("IMP-", StringComparison.Ordinal)).Distinct().Take(20).ToList();
			var context = new
			{
				workspace = workspaceId,
				dataset = datasetId,
				run = runId,
				codes = codes.Select(c => new { code = c, count = openIssues.Count(i => i.Code == c) }),
				blocking = openIssues.Count(i => i.Severity == (int)ImportIssueSeverity.Blocking)
			};

			var now = DateTime.UtcNow;
			var request = new ImportSupportRequest
			{
				DepartmentId = departmentId,
				WorkspaceId = workspaceId,
				Number = await NewNumberAsync(cancellationToken),
				DatasetId = datasetId,
				RunId = runId,
				IssueCodes = Clip(string.Join(",", codes), 1000),
				Category = (int)category,
				Subject = Clip(subject.Trim(), 200),
				Description = Clip(description?.Trim(), 4000),
				Priority = (int)(category == ImportSupportRequestCategory.Error ? ImportSupportRequestPriority.High : ImportSupportRequestPriority.Normal),
				Status = (int)ImportSupportRequestStatus.Open,
				Context = JsonConvert.SerializeObject(context),
				HelpdeskState = (int)(_helpdesk.IsConfigured ? ImportHelpdeskState.Pending : ImportHelpdeskState.NotConfigured),
				OpenedBy = userId,
				OpenedOn = now,
				UpdatedOn = now
			};
			await _requests.InsertAsync(request, cancellationToken, true);

			await _messages.InsertAsync(new ImportMessage
			{
				DepartmentId = departmentId,
				WorkspaceId = workspaceId,
				SupportRequestId = request.Id,
				AuthorKind = (int)ImportActorKind.Administrator,
				AuthorIdentity = userId,
				Body = Clip($"{request.Subject}\n\n{request.Description}".Trim(), MaxMessageLength),
				CreatedOn = now,
				ReadByDepartmentOn = now
			}, cancellationToken, true);

			await _timeline.RecordAsync(departmentId, workspaceId, ImportActorKind.Administrator, userId, "SupportRequestOpened",
				new { request.Number, category = category.ToString() }, cancellationToken: cancellationToken);
			return request;
		}

		public Task<List<ImportSupportRequest>> GetRequestsAsync(int departmentId, string workspaceId, CancellationToken cancellationToken = default) =>
			_requests.GetForWorkspaceAsync(departmentId, workspaceId, cancellationToken);

		public async Task<ImportSupportRequest> ConfirmOrReopenAsync(int departmentId, string userId, string requestId, bool reopen, CancellationToken cancellationToken = default)
		{
			var request = await _requests.GetByIdAsync(departmentId, requestId, cancellationToken) ?? throw new DepartmentImportException("import_request_not_found", 404);
			var now = DateTime.UtcNow;
			if (reopen)
			{
				request.Status = (int)ImportSupportRequestStatus.Open;
				request.ResolvedOn = null;
				request.ClosedOn = null;
			}
			else
			{
				request.Status = (int)ImportSupportRequestStatus.Closed;
				request.ClosedOn = now;
			}
			request.UpdatedOn = now;
			await _requests.UpdateAsync(request, cancellationToken, true);
			await _timeline.RecordAsync(departmentId, request.WorkspaceId, ImportActorKind.Administrator, userId, reopen ? "SupportRequestReopened" : "SupportRequestClosed",
				new { request.Number }, cancellationToken: cancellationToken);
			return request;
		}

		public async Task<ImportMessage> PostMessageAsync(int departmentId, string userId, string workspaceId, string body, string supportRequestId,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(body))
				throw new DepartmentImportException("import_message_empty");
			if (await _workspaces.GetByIdAsync(departmentId, workspaceId, cancellationToken) == null)
				throw new DepartmentImportException("import_workspace_not_found", 404);
			if (supportRequestId != null && await _requests.GetByIdAsync(departmentId, supportRequestId, cancellationToken) == null)
				throw new DepartmentImportException("import_request_not_found", 404);

			var now = DateTime.UtcNow;
			var message = new ImportMessage
			{
				DepartmentId = departmentId,
				WorkspaceId = workspaceId,
				SupportRequestId = supportRequestId,
				AuthorKind = (int)ImportActorKind.Administrator,
				AuthorIdentity = userId,
				// Plain text; HTML-encoded at render, never interpreted as markup (section 17.5).
				Body = Clip(body.Trim(), MaxMessageLength),
				CreatedOn = now,
				ReadByDepartmentOn = now
			};
			await _messages.InsertAsync(message, cancellationToken, true);
			await _workspaces.TouchAsync(departmentId, workspaceId, now, cancellationToken);
			return message;
		}

		public async Task<List<ImportMessage>> GetThreadAsync(int departmentId, string workspaceId, int skip, int take, bool markRead, CancellationToken cancellationToken = default)
		{
			var page = await _messages.GetPageAsync(departmentId, workspaceId, Math.Max(0, skip), Math.Clamp(take, 1, 200), cancellationToken);
			if (markRead)
				await _messages.MarkReadByDepartmentAsync(departmentId, workspaceId, DateTime.UtcNow, cancellationToken);
			return page;
		}

		public Task<List<ImportSuggestion>> GetSuggestionsAsync(int departmentId, string workspaceId, CancellationToken cancellationToken = default) =>
			_suggestions.GetProposedAsync(departmentId, workspaceId, cancellationToken);

		public async Task<ImportSuggestion> DecideSuggestionAsync(int departmentId, string userId, string suggestionId, bool accept, CancellationToken cancellationToken = default)
		{
			var suggestion = await _suggestions.GetByIdAsync(departmentId, suggestionId, cancellationToken) ?? throw new DepartmentImportException("import_suggestion_not_found", 404);
			if (suggestion.Status != (int)ImportSuggestionStatus.Proposed)
				throw new DepartmentImportException("import_suggestion_decided", 409);

			if (accept)
			{
				// Accepting is the administrator's action, attributed to them as an accepted suggestion; the normal
				// re-validation and approval rules apply to whatever it changes.
				switch ((ImportSuggestionKind)suggestion.Kind)
				{
					case ImportSuggestionKind.ColumnMap:
					{
						var body = JsonConvert.DeserializeObject<ImportColumnMapBody>(suggestion.Payload ?? "{}");
						var mapping = await _mapping.GetMappingAsync(departmentId, suggestion.DatasetId, cancellationToken);
						await _mapping.SaveColumnMapAsync(departmentId, userId, ImportActorKind.SupportSuggestion, suggestion.DatasetId, body, mapping.Dataset.Revision, cancellationToken);
						break;
					}
					case ImportSuggestionKind.ValueMap:
					{
						var payload = JsonConvert.DeserializeObject<ValueMapSuggestion>(suggestion.Payload ?? "{}");
						await _mapping.SaveValueMapsAsync(departmentId, userId, ImportActorKind.SupportSuggestion, suggestion.DatasetId, payload.FieldKey,
							payload.Decisions ?? new List<ImportValueDecision>(), cancellationToken);
						break;
					}
					case ImportSuggestionKind.IssueResolution:
					{
						var payload = JsonConvert.DeserializeObject<IssueSuggestion>(suggestion.Payload ?? "{}");
						if (payload.Action == "accept")
							await _issueService.AcceptAsync(departmentId, userId, ImportActorKind.SupportSuggestion, suggestion.WorkspaceId, payload.IssueIds, cancellationToken);
						else if (payload.Action == "exclude")
							await _issueService.ExcludeRowsAsync(departmentId, userId, ImportActorKind.SupportSuggestion, suggestion.WorkspaceId, payload.IssueIds, cancellationToken);
						else if (payload.Action == "set-value")
							await _issueService.SetValueForIssuesAsync(departmentId, userId, ImportActorKind.SupportSuggestion, suggestion.WorkspaceId, payload.IssueIds,
								payload.Value, cancellationToken);
						break;
					}
				}
			}

			suggestion.Status = (int)(accept ? ImportSuggestionStatus.Accepted : ImportSuggestionStatus.Rejected);
			suggestion.DecidedBy = userId;
			suggestion.DecidedOn = DateTime.UtcNow;
			await _suggestions.UpdateAsync(suggestion, cancellationToken, true);
			await _timeline.RecordAsync(departmentId, suggestion.WorkspaceId, ImportActorKind.Administrator, userId, accept ? "SuggestionAccepted" : "SuggestionRejected",
				new { suggestionId, kind = suggestion.Kind }, cancellationToken: cancellationToken);
			return suggestion;
		}

		public async Task<int> ProcessDueAsync(CancellationToken cancellationToken = default)
		{
			if (!_helpdesk.IsConfigured)
				return 0;

			var created = 0;
			foreach (var request in await _requests.GetHelpdeskDueAsync(DateTime.UtcNow, 25, cancellationToken))
			{
				var ticket = await _helpdesk.CreateTicketAsync(await TicketAsync(request, cancellationToken), cancellationToken);
				request.HelpdeskAttempts++;
				if (ticket.Success)
				{
					request.HelpdeskState = (int)ImportHelpdeskState.Created;
					request.HelpdeskTicketId = ticket.TicketId;
					request.HelpdeskTicketUrl = ticket.TicketUrl;
					request.HelpdeskNextAttemptOn = null;
					created++;
				}
				else if (ticket.Transient && request.HelpdeskAttempts < 10)
				{
					request.HelpdeskNextAttemptOn = DateTime.UtcNow.Add(DepartmentImportStateMachine.Backoff(request.HelpdeskAttempts));
				}
				else
				{
					request.HelpdeskState = (int)ImportHelpdeskState.Failed;
					Logging.LogError($"Helpdesk ticket for import support request {request.Number} failed permanently ({ticket.ErrorCode}).");
				}

				request.UpdatedOn = DateTime.UtcNow;
				await _requests.UpdateAsync(request, cancellationToken, true);
			}

			return created + await MirrorDueNotesAsync(cancellationToken);
		}

		/// <summary>Staff replies on a request copied to its helpdesk ticket as private notes (section 17.5).</summary>
		private async Task<int> MirrorDueNotesAsync(CancellationToken cancellationToken)
		{
			var mirrored = 0;
			foreach (var message in await _messages.GetHelpdeskDueAsync(DateTime.UtcNow, 25, cancellationToken))
			{
				var request = string.IsNullOrWhiteSpace(message.SupportRequestId)
					? null
					: await _requests.GetByIdAsync(message.DepartmentId, message.SupportRequestId, cancellationToken);
				if (request == null || request.HelpdeskState == (int)ImportHelpdeskState.NotConfigured || request.HelpdeskState == (int)ImportHelpdeskState.Failed)
				{
					// No ticket exists or will exist: nothing to mirror to.
					message.HelpdeskState = (int)ImportHelpdeskState.NotConfigured;
					message.HelpdeskNextAttemptOn = null;
				}
				else if (request.HelpdeskState == (int)ImportHelpdeskState.Pending || string.IsNullOrWhiteSpace(request.HelpdeskTicketId))
				{
					// The ticket is still being created: wait for it without spending an attempt.
					message.HelpdeskNextAttemptOn = DateTime.UtcNow.AddMinutes(5);
				}
				else
				{
					message.HelpdeskAttempts++;
					if (await _helpdesk.AddNoteAsync(request.HelpdeskTicketId, NoteBody(message), cancellationToken))
					{
						message.HelpdeskState = (int)ImportHelpdeskState.Created;
						message.HelpdeskNextAttemptOn = null;
						mirrored++;
					}
					else if (message.HelpdeskAttempts < 10)
					{
						message.HelpdeskNextAttemptOn = DateTime.UtcNow.Add(DepartmentImportStateMachine.Backoff(message.HelpdeskAttempts));
					}
					else
					{
						message.HelpdeskState = (int)ImportHelpdeskState.Failed;
						message.HelpdeskNextAttemptOn = null;
						Logging.LogError($"Helpdesk note for import support request {request.Number} failed permanently.");
					}
				}

				await _messages.UpdateAsync(message, cancellationToken, true);
			}

			return mirrored;
		}

		/// <summary>The note: who replied and the reply itself (staff-written plain text; the provider HTML-encodes it and turns line breaks into <br>).</summary>
		public static string NoteBody(ImportMessage message) =>
			$"Resgrid support ({message.AuthorIdentity}) replied in the Import & Migration workspace:\n\n{message.Body}";

		/// <summary>The helpdesk ticket: department, category, the administrator's own words, issue codes and counts and the BackOffice link — nothing else.</summary>
		public async Task<HelpdeskTicketRequest> TicketAsync(ImportSupportRequest request, CancellationToken cancellationToken)
		{
			var department = await _departments.GetDepartmentByIdAsync(request.DepartmentId, false);
			var user = string.IsNullOrWhiteSpace(request.OpenedBy) ? null : _users.GetUserById(request.OpenedBy, false);
			var profile = user == null ? null : await _profiles.GetProfileByUserIdAsync(user.Id, false);

			var description = new StringBuilder();
			description.AppendLine($"Department: {department?.Name} (id {request.DepartmentId})");
			description.AppendLine($"Request: {request.Number} · {(ImportSupportRequestCategory)request.Category}");
			description.AppendLine();
			description.AppendLine(request.Description ?? string.Empty);
			description.AppendLine();
			if (!string.IsNullOrWhiteSpace(request.Context))
				description.AppendLine("Context: " + request.Context);
			if (!string.IsNullOrWhiteSpace(SupportConfig.BackOfficeBaseUrl))
				description.AppendLine($"BackOffice: {SupportConfig.BackOfficeBaseUrl.TrimEnd('/')}/imports/{request.WorkspaceId}");

			return new HelpdeskTicketRequest
			{
				RequesterEmail = user?.Email,
				RequesterName = profile?.FullName?.AsFirstNameLastName,
				Subject = $"[Import] {request.Subject}",
				Description = description.ToString(),
				Priority = request.Priority,
				Tags = new List<string> { "department-import", ((ImportSupportRequestCategory)request.Category).ToString().ToLowerInvariant() }
			};
		}

		private async Task<string> NewNumberAsync(CancellationToken cancellationToken)
		{
			const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
			for (var attempt = 0; attempt < 10; attempt++)
			{
				var number = "IR-" + new string(Enumerable.Range(0, 6).Select(_ => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]).ToArray());
				if (!await _requests.NumberExistsAsync(number, cancellationToken))
					return number;
			}

			throw new DepartmentImportException("import_request_number_unavailable", 503);
		}

		private void Audit(int departmentId, string userId, AuditLogTypes type, object after, string ipAddress)
		{
			_events.SendMessage(new AuditEvent
			{
				DepartmentId = departmentId,
				UserId = userId,
				Type = type,
				After = JsonConvert.SerializeObject(after),
				IpAddress = ipAddress,
				Successful = true,
				ServerName = Environment.MachineName
			});
		}

		private static string Clip(string value, int length) =>
			string.IsNullOrEmpty(value) || value.Length <= length ? value : value.Substring(0, length);

		private sealed class ValueMapSuggestion
		{
			public string FieldKey { get; set; }
			public List<ImportValueDecision> Decisions { get; set; }
		}

		private sealed class IssueSuggestion
		{
			public string Action { get; set; }
			public List<string> IssueIds { get; set; }
			public string Value { get; set; }
		}
	}
}
