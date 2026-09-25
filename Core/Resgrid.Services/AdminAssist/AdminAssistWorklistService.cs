using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.AdminAssist;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.AdminAssist
{
	public sealed class AdminAssistWorklistService(IAdminAssistAccessService access, IAdminAssistService assist,
		IAdminAssistRepository repository, IUnitOfWork unit, IDomainEventOutboxService outbox,
		IRecordsAuthorizationService authorization, IProtectedGrantContext grant, Lazy<IProtectedWriteService> write,
		Lazy<IProtectedReadService> read, IDepartmentDataProtectionService protection, TimeProvider clock,
		IAuditLogsRepository audits) : IAdminAssistWorklistService
	{
		private static readonly IReadOnlyDictionary<string, (Func<AdminAssistFindingRow, string>, Action<AdminAssistFindingRow, string>)> Fields =
			new Dictionary<string, (Func<AdminAssistFindingRow, string>, Action<AdminAssistFindingRow, string>)>
			{ ["adminassistfindings.content"] = (row => row.Content, (row, value) => row.Content = value) };

		public async Task<IReadOnlyList<AdminAssistFindingRow>> GetAsync(AdminAssistActor actor, CancellationToken ct = default)
		{
			await RequireAsync(actor, ct);
			var rows = await repository.GetFindingsAsync(actor.DepartmentId, ct);
			if (await protection.IsProtectionEnforcedAsync(actor.DepartmentId) && await protection.GetPinnedCatalogVersionAsync(actor.DepartmentId) < 30)
			{
				foreach (var row in rows) if (!string.IsNullOrEmpty(row.Content)) row.Content = ProtectedDataEnvelope.RedactionValue;
			}
			else if (rows.Any(row => !string.IsNullOrEmpty(row.Content)))
				await read.Value.ResolveRecordsEntitiesForReadAsync(actor.DepartmentId, rows.Select(row => (row, row.AdminAssistFindingId)).ToArray(), Fields, grant.GrantToken, actor.UserId, ct);
			await RequireAsync(actor, ct);
			return rows;
		}

		public async Task VerifyAsync(AdminAssistActor actor, CancellationToken ct = default)
		{
			await RequireAsync(actor, ct);
			var overview = await assist.GetOverviewAsync(actor, false, ct);
			if (!overview.Report.Snapshot.Consistent || !long.TryParse(overview.Report.Snapshot.Revision, out var revision)) throw new AdminAssistConcurrencyException();
			if (unit.Transaction != null) throw new InvalidOperationException("Verification owns its metadata transaction.");
			var events = new List<long>();
			await unit.CreateOrGetConnectionAsync(ct);
			try
			{
				await repository.LockConfigurationAsync(actor.DepartmentId, ct);
				if (await repository.GetConfigurationRevisionAsync(actor.DepartmentId, ct) != revision) throw new AdminAssistConcurrencyException();
				await RequireAsync(actor, ct);
				var existing = (await repository.GetFindingsAsync(actor.DepartmentId, ct)).ToDictionary(r => r.RuleId, StringComparer.Ordinal);
				var now = clock.GetUtcNow().UtcDateTime;
				foreach (var finding in overview.Report.Findings)
				{
					if (!existing.TryGetValue(finding.RuleId, out var row)) row = new AdminAssistFindingRow
					{
						AdminAssistFindingId = Guid.NewGuid().ToString("D"), DepartmentId = actor.DepartmentId,
						RuleId = finding.RuleId, SubjectId = "department", Result = (int)RuleResult.Unknown
					};
					var expected = row.Revision;
					var trigger = FindingLifecycle.Observe(row, finding, now);
					await repository.SaveFindingAsync(row, expected, ct);
					if (trigger.HasValue)
					{
						var message = await outbox.EnqueueAsync(actor.DepartmentId, "AdminAssist", new DomainEventEnvelope
						{
							EventName = trigger.Value.ToString(), AggregateType = "AdminAssistFinding", AggregateId = row.AdminAssistFindingId,
							AggregateVersion = checked((int)row.Revision), Trigger = trigger, OccurredOn = now,
							Payload = new { FindingId = row.AdminAssistFindingId, row.RuleId, row.Episode, row.Result, row.Severity, row.ReviewStatus }
						}, ct);
						events.Add(message.DomainEventOutboxId);
					}
				}
				await repository.SaveDailySummaryAsync(actor.DepartmentId, overview.Report, overview.CatalogVersion, ct);
				unit.CommitChanges();
			}
			catch { unit.DiscardChanges(); throw; }
			await outbox.DispatchAfterCommitAsync(events, ct);
		}

		public async Task ReviewAsync(AdminAssistActor actor, FindingReviewCommand command, CancellationToken ct = default)
		{
			await RequireAsync(actor, ct);
			var now = clock.GetUtcNow().UtcDateTime;
			if (command == null || !Guid.TryParseExact(command.FindingId, "D", out _) || command.ExpectedRevision <= 0 || command.Note?.Length > 2000 ||
				(command.ReviewOnUtc.HasValue && command.ReviewOnUtc.Value.Kind != DateTimeKind.Utc) ||
				(command.ExceptionUntilUtc.HasValue && command.ExceptionUntilUtc.Value.Kind != DateTimeKind.Utc) ||
				command.ReviewOnUtc < now || command.ReviewOnUtc > now.AddYears(1)) throw new ArgumentException("Invalid review.");
			if (command.OwnerId != null && (!await authorization.IsAssignableMemberAsync(command.OwnerId, actor.DepartmentId) ||
				!await authorization.IsDepartmentAdminAsync(command.OwnerId, actor.DepartmentId))) throw new ArgumentException("Invalid review owner.");
			if (unit.Transaction != null) throw new InvalidOperationException("Review owns its metadata transaction.");
			await unit.CreateOrGetConnectionAsync(ct);
			try
			{
				await repository.LockConfigurationAsync(actor.DepartmentId, ct);
				var row = (await repository.GetFindingsAsync(actor.DepartmentId, ct)).SingleOrDefault(r => r.AdminAssistFindingId == command.FindingId);
				if (row == null) throw new ArgumentException("Finding not found.");
				if (row.ReviewStatus == (int)FindingReviewStatus.Resolved || row.Result == (int)RuleResult.Pass || row.Result == (int)RuleResult.NotApplicable)
					throw new ArgumentException("Only unresolved findings can enter review.");
				if (row.Revision != command.ExpectedRevision) throw new AdminAssistConcurrencyException();
				var previous = JsonConvert.DeserializeObject<AdminAssistFindingRow>(JsonConvert.SerializeObject(row));
				var beforeCode = ((FindingReviewStatus)row.ReviewStatus).ToString();
				switch (command.Operation)
				{
					case "claim": row.OwnerId = actor.UserId; row.ReviewOn = command.ReviewOnUtc; row.ReviewStatus = (int)FindingReviewStatus.Assigned; row.ExceptionUntil = null; break;
					case "assign": row.OwnerId = command.OwnerId; row.ReviewOn = command.ReviewOnUtc; row.ReviewStatus = (int)(row.OwnerId == null ? FindingReviewStatus.Unassigned : FindingReviewStatus.Assigned); row.ExceptionUntil = null; break;
					case "review": row.ReviewStatus = (int)FindingReviewStatus.InReview; break;
					case "exception":
						if (row.Result != (int)RuleResult.Fail || string.IsNullOrWhiteSpace(command.Note) || !command.ExceptionUntilUtc.HasValue || command.ExceptionUntilUtc <= now || command.ExceptionUntilUtc > now.AddDays(90)) throw new ArgumentException("An exception needs a reason and an expiry within 90 days.");
						row.ReviewStatus = (int)FindingReviewStatus.AcceptedException; row.ExceptionUntil = command.ExceptionUntilUtc; break;
					default: throw new ArgumentException("Invalid review operation.");
				}
				if (command.Note != null)
				{
					if (await protection.ShouldEncryptNewWritesAsync(actor.DepartmentId) && await protection.GetPinnedCatalogVersionAsync(actor.DepartmentId) < 30)
						throw new UnauthorizedAccessException();
					row.Content = command.Note;
					var prepared = await write.Value.PrepareRecordsEntityWriteAsync(actor.DepartmentId, row, previous, row.AdminAssistFindingId,
						Fields, () => { row.IsProtected = true; row.ProtectedCatalogVersion = 30; }, grant.GrantToken, actor.UserId, false, ct);
					if (prepared?.Success != true || prepared.IsProtected && !ProtectedDataEnvelope.HasEnvelopePrefix(row.Content)) throw new UnauthorizedAccessException();
				}
				row.Revision++;
				await RequireAsync(actor, ct);
				await repository.SaveFindingAsync(row, command.ExpectedRevision, ct);
				await repository.AppendConfigurationChangeAsync(actor.DepartmentId, actor.UserId, "finding." + row.AdminAssistFindingId, beforeCode,
					((FindingReviewStatus)row.ReviewStatus).ToString(), Guid.NewGuid().ToString("N"), ct);
				await audits.SaveOrUpdateAsync(new AuditLog
				{
					DepartmentId = actor.DepartmentId, ObjectDepartmentId = actor.DepartmentId, UserId = actor.UserId,
					LogType = (int)AuditLogTypes.AdminAssistReviewChanged, LoggedOn = now, Successful = true,
					ObjectId = row.AdminAssistFindingId, Message = "AdminAssistReviewChanged",
					Data = JsonConvert.SerializeObject(new { row.RuleId, row.Revision, operation = command.Operation,
						before = beforeCode, after = ((FindingReviewStatus)row.ReviewStatus).ToString(), noteChanged = command.Note != null })
				}, ct);
				unit.CommitChanges();
			}
			catch { unit.DiscardChanges(); throw; }
		}
		private async Task RequireAsync(AdminAssistActor actor, CancellationToken ct)
		{
			if (!await access.CanAccessAsync(actor, false, ct)) throw new UnauthorizedAccessException();
		}
	}
}
