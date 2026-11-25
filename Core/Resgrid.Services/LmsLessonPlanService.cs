using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Lms;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Lesson plans (§13 T4 item 5, R-C6; LMS add-on) on the content core's P1 lifecycle (<see cref="ProtocolLifecycle"/>): a draft is
	/// edited, submitted with a change summary (its content hash fixed), approved or sent back, then published as 1.0 / major / minor; the
	/// previous published version is superseded and every version that was ever submitted is kept. Resgrid starter plans are copied into a
	/// department draft. The callers check the LMS gate and the person's rights (approval and publication need Manage Courses).
	/// </summary>
	public class LmsLessonPlanService : ILmsLessonPlanService
	{
		private readonly ILmsLessonPlansRepository _plans;
		private readonly ILmsLessonPlanVersionsRepository _versions;
		private readonly IEventAggregator _events;

		public LmsLessonPlanService(ILmsLessonPlansRepository plans, ILmsLessonPlanVersionsRepository versions, IEventAggregator events)
		{
			_plans = plans;
			_versions = versions;
			_events = events;
		}

		public IReadOnlyList<LmsLessonPlanStarter> GetStarters() => LmsLessonPlanStarterCatalog.All;

		public async Task<List<LmsLessonPlan>> GetPlansAsync(int departmentId) =>
			departmentId <= 0 ? new List<LmsLessonPlan>() : await _plans.GetForDepartmentAsync(departmentId) ?? new List<LmsLessonPlan>();

		public async Task<LmsLessonPlan> GetPlanAsync(int departmentId, int lmsLessonPlanId)
		{
			var plan = await _plans.GetByIdForDepartmentAsync(departmentId, lmsLessonPlanId);
			return plan == null || plan.IsDeleted || plan.DepartmentId != departmentId ? null : plan;
		}

		public async Task<List<LmsLessonPlanVersion>> GetVersionsAsync(int departmentId, int lmsLessonPlanId) =>
			(await _versions.GetForPlanAsync(departmentId, lmsLessonPlanId) ?? new List<LmsLessonPlanVersion>()).Where(v => v.DepartmentId == departmentId).ToList();

		public async Task<LmsLessonPlanVersion> GetVersionAsync(int departmentId, int lmsLessonPlanVersionId)
		{
			var version = await _versions.GetByIdForDepartmentAsync(departmentId, lmsLessonPlanVersionId);
			return version == null || version.DepartmentId != departmentId ? null : version;
		}

		private async Task<LmsLessonPlanVersion> RequireVersionAsync(int departmentId, int lmsLessonPlanVersionId)
		{
			var version = await GetVersionAsync(departmentId, lmsLessonPlanVersionId) ?? throw new InvalidOperationException("lms_class_plan_not_found");
			_ = await GetPlanAsync(departmentId, version.LmsLessonPlanId) ?? throw new InvalidOperationException("lms_class_plan_not_found");
			return version;
		}

		public async Task<LmsLessonPlan> CreateAsync(int departmentId, string title, LmsLessonPlanContent content, string userId, CancellationToken cancellationToken = default) =>
			await CreateCoreAsync(departmentId, title, content, null, userId, cancellationToken);

		public async Task<LmsLessonPlan> ApplyStarterAsync(int departmentId, string starterKey, string userId, CancellationToken cancellationToken = default)
		{
			var starter = LmsLessonPlanStarterCatalog.Find(starterKey) ?? throw new InvalidOperationException("lms_class_plan_not_found");
			return await CreateCoreAsync(departmentId, starter.Title, starter.Content, starter.Key, userId, cancellationToken);
		}

		private async Task<LmsLessonPlan> CreateCoreAsync(int departmentId, string title, LmsLessonPlanContent content, string starterKey, string userId, CancellationToken cancellationToken)
		{
			if (departmentId <= 0)
				throw new InvalidOperationException("lms_class_plan_not_found");
			var name = LmsClassService.Trim(title, LmsLessonPlan.TitleMaxLength) ?? throw new InvalidOperationException("lms_class_title_required");
			var now = DateTime.UtcNow;
			var plan = await _plans.InsertAsync(new LmsLessonPlan
			{
				DepartmentId = departmentId,
				Title = name,
				StarterKey = starterKey,
				CreatedOn = now,
				CreatedByUserId = userId
			}, cancellationToken, true);
			await _versions.InsertAsync(new LmsLessonPlanVersion
			{
				LmsLessonPlanId = plan.LmsLessonPlanId,
				DepartmentId = departmentId,
				Status = (int)ProtocolVersionStatuses.Draft,
				Title = name,
				ContentJson = (content ?? new LmsLessonPlanContent()).ToJson(),
				CreatedOn = now,
				CreatedByUserId = userId
			}, cancellationToken, true);
			Audit(departmentId, userId, AuditLogTypes.LmsLessonPlanChanged, null, JsonConvert.SerializeObject(new { plan.LmsLessonPlanId, plan.Title, starterKey }));
			return plan;
		}

		public async Task<LmsLessonPlanVersion> SaveDraftAsync(int departmentId, int lmsLessonPlanVersionId, string title, LmsLessonPlanContent content, string userId,
			CancellationToken cancellationToken = default)
		{
			var version = await RequireVersionAsync(departmentId, lmsLessonPlanVersionId);
			if (!ProtocolLifecycle.IsContentEditable(version.Status))
				throw new InvalidOperationException("lms_class_plan_not_draft");
			version.Title = LmsClassService.Trim(title, LmsLessonPlan.TitleMaxLength) ?? throw new InvalidOperationException("lms_class_title_required");
			version.ContentJson = (content ?? new LmsLessonPlanContent()).ToJson();
			version.UpdatedOn = DateTime.UtcNow;
			var stored = await _versions.SaveOrUpdateAsync(version, cancellationToken, true);

			var plan = await GetPlanAsync(departmentId, version.LmsLessonPlanId);
			if (plan != null && !plan.CurrentVersionId.HasValue && plan.Title != stored.Title)
			{
				plan.Title = stored.Title;
				plan.UpdatedOn = DateTime.UtcNow;
				plan.UpdatedByUserId = userId;
				await _plans.SaveOrUpdateAsync(plan, cancellationToken, true);
			}

			Audit(departmentId, userId, AuditLogTypes.LmsLessonPlanChanged, null, JsonConvert.SerializeObject(new { stored.LmsLessonPlanId, stored.LmsLessonPlanVersionId, stored.Title }));
			return stored;
		}

		public async Task<LmsLessonPlanVersion> SubmitAsync(int departmentId, int lmsLessonPlanVersionId, string changeSummary, string userId, CancellationToken cancellationToken = default)
		{
			var version = await RequireVersionAsync(departmentId, lmsLessonPlanVersionId);
			var summary = LmsClassService.Trim(changeSummary, LmsLessonPlanVersion.SummaryMaxLength) ?? throw new InvalidOperationException("lms_class_change_summary_required");
			if (!ProtocolLifecycle.CanMove(version.Status, (int)ProtocolVersionStatuses.InReview) ||
				!await _versions.MoveAsync(departmentId, version.LmsLessonPlanVersionId, (int)ProtocolVersionStatuses.Draft, (int)ProtocolVersionStatuses.InReview, cancellationToken))
				throw new InvalidOperationException("lms_class_plan_not_draft");

			version.Status = (int)ProtocolVersionStatuses.InReview;
			version.ChangeSummary = summary;
			version.ContentHash = LmsClassRules.Hash(version.ContentJson);
			version.SubmittedOn = DateTime.UtcNow;
			version.SubmittedByUserId = userId;
			version.DecidedOn = null;
			version.DecidedByUserId = null;
			version.DecisionNote = null;
			version.UpdatedOn = DateTime.UtcNow;
			var stored = await _versions.SaveOrUpdateAsync(version, cancellationToken, true);
			Audit(departmentId, userId, AuditLogTypes.LmsLessonPlanSubmitted, null, JsonConvert.SerializeObject(new { stored.LmsLessonPlanId, stored.LmsLessonPlanVersionId, stored.ContentHash }));
			return stored;
		}

		public async Task<LmsLessonPlanVersion> DecideAsync(int departmentId, int lmsLessonPlanVersionId, bool approve, string note, string userId, CancellationToken cancellationToken = default)
		{
			var version = await RequireVersionAsync(departmentId, lmsLessonPlanVersionId);
			var text = LmsClassService.Trim(note, LmsLessonPlanVersion.SummaryMaxLength);
			if (!approve && text == null)
				throw new InvalidOperationException("lms_class_reason_required");
			var target = approve ? ProtocolVersionStatuses.Approved : ProtocolVersionStatuses.Draft;
			// The approval is bound to the content that was submitted: the stored content must still hash to the submitted fingerprint.
			if (approve && !string.Equals(version.ContentHash, LmsClassRules.Hash(version.ContentJson), StringComparison.Ordinal))
				throw new InvalidOperationException("lms_class_plan_changed");
			if (!ProtocolLifecycle.CanMove(version.Status, (int)target) ||
				!await _versions.MoveAsync(departmentId, version.LmsLessonPlanVersionId, (int)ProtocolVersionStatuses.InReview, (int)target, cancellationToken))
				throw new InvalidOperationException("lms_class_plan_not_in_review");

			version.Status = (int)target;
			version.DecidedOn = DateTime.UtcNow;
			version.DecidedByUserId = userId;
			version.DecisionNote = text;
			version.UpdatedOn = DateTime.UtcNow;
			var stored = await _versions.SaveOrUpdateAsync(version, cancellationToken, true);
			Audit(departmentId, userId, AuditLogTypes.LmsLessonPlanDecided, null,
				JsonConvert.SerializeObject(new { stored.LmsLessonPlanId, stored.LmsLessonPlanVersionId, approved = approve, note = text }));
			return stored;
		}

		public async Task<LmsLessonPlanVersion> PublishAsync(int departmentId, int lmsLessonPlanVersionId, bool major, string userId, CancellationToken cancellationToken = default)
		{
			var version = await RequireVersionAsync(departmentId, lmsLessonPlanVersionId);
			var plan = await GetPlanAsync(departmentId, version.LmsLessonPlanId) ?? throw new InvalidOperationException("lms_class_plan_not_found");
			if (!ProtocolLifecycle.CanMove(version.Status, (int)ProtocolVersionStatuses.Published) ||
				!await _versions.MoveAsync(departmentId, version.LmsLessonPlanVersionId, (int)ProtocolVersionStatuses.Approved, (int)ProtocolVersionStatuses.Published, cancellationToken))
				throw new InvalidOperationException("lms_class_plan_not_approved");

			var current = plan.CurrentVersionId.HasValue ? await GetVersionAsync(departmentId, plan.CurrentVersionId.Value) : null;
			var (number, minor) = ProtocolLifecycle.NextNumber(current?.Major ?? 0, current?.Minor ?? 0, major);
			version.Status = (int)ProtocolVersionStatuses.Published;
			version.Major = number;
			version.Minor = minor;
			version.PublishedOn = DateTime.UtcNow;
			version.PublishedByUserId = userId;
			version.UpdatedOn = DateTime.UtcNow;
			var stored = await _versions.SaveOrUpdateAsync(version, cancellationToken, true);

			if (current != null && current.LmsLessonPlanVersionId != stored.LmsLessonPlanVersionId)
				await _versions.MoveAsync(departmentId, current.LmsLessonPlanVersionId, (int)ProtocolVersionStatuses.Published, (int)ProtocolVersionStatuses.Superseded, cancellationToken);

			plan.CurrentVersionId = stored.LmsLessonPlanVersionId;
			plan.Title = stored.Title;
			plan.UpdatedOn = DateTime.UtcNow;
			plan.UpdatedByUserId = userId;
			await _plans.SaveOrUpdateAsync(plan, cancellationToken, true);
			Audit(departmentId, userId, AuditLogTypes.LmsLessonPlanPublished, null,
				JsonConvert.SerializeObject(new { stored.LmsLessonPlanId, stored.LmsLessonPlanVersionId, number = stored.Number }));
			return stored;
		}

		public async Task<LmsLessonPlanVersion> NewDraftAsync(int departmentId, int lmsLessonPlanId, string userId, CancellationToken cancellationToken = default)
		{
			var plan = await GetPlanAsync(departmentId, lmsLessonPlanId) ?? throw new InvalidOperationException("lms_class_plan_not_found");
			var versions = await GetVersionsAsync(departmentId, lmsLessonPlanId);
			var open = versions.FirstOrDefault(v => v.Status == (int)ProtocolVersionStatuses.Draft || v.Status == (int)ProtocolVersionStatuses.InReview ||
				v.Status == (int)ProtocolVersionStatuses.Approved);
			if (open != null)
				return open;

			var source = plan.CurrentVersionId.HasValue ? versions.FirstOrDefault(v => v.LmsLessonPlanVersionId == plan.CurrentVersionId.Value) : versions.FirstOrDefault();
			var stored = await _versions.InsertAsync(new LmsLessonPlanVersion
			{
				LmsLessonPlanId = plan.LmsLessonPlanId,
				DepartmentId = departmentId,
				Status = (int)ProtocolVersionStatuses.Draft,
				Title = source?.Title ?? plan.Title,
				ContentJson = source?.ContentJson ?? new LmsLessonPlanContent().ToJson(),
				CreatedOn = DateTime.UtcNow,
				CreatedByUserId = userId
			}, cancellationToken, true);
			Audit(departmentId, userId, AuditLogTypes.LmsLessonPlanChanged, null, JsonConvert.SerializeObject(new { stored.LmsLessonPlanId, stored.LmsLessonPlanVersionId, draft = true }));
			return stored;
		}

		public async Task<bool> DeleteAsync(int departmentId, int lmsLessonPlanId, string userId, CancellationToken cancellationToken = default)
		{
			var plan = await GetPlanAsync(departmentId, lmsLessonPlanId);
			if (plan == null)
				return false;
			// Soft delete: sessions keep the version they were taught from (the OSHA roster), and every submitted version is kept.
			plan.IsDeleted = true;
			plan.UpdatedOn = DateTime.UtcNow;
			plan.UpdatedByUserId = userId;
			await _plans.SaveOrUpdateAsync(plan, cancellationToken, true);
			Audit(departmentId, userId, AuditLogTypes.LmsLessonPlanChanged, JsonConvert.SerializeObject(new { plan.LmsLessonPlanId, plan.Title }), null);
			return true;
		}

		private void Audit(int departmentId, string userId, AuditLogTypes type, string before, string after)
		{
			_events?.SendMessage(new AuditEvent
			{
				DepartmentId = departmentId,
				UserId = userId,
				Type = type,
				Before = before,
				After = after,
				Successful = true,
				ServerName = Environment.MachineName
			});
		}
	}
}
