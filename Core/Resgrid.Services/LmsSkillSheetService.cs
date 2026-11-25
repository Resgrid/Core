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
	/// Skill sheet authoring (T4 item 2, LMS add-on) on the content core's version shape: a sheet has at most one draft and one published
	/// version; publishing supersedes the previous one and an evaluation keeps the version it started on. Authoring needs an LMS manager
	/// (Manage Courses or a department admin) and the add-on ("can author"); reading never does.
	/// </summary>
	public class LmsSkillSheetService : ILmsSkillSheetService
	{
		private readonly ILmsSkillSheetsRepository _sheets;
		private readonly ILmsSkillSheetVersionsRepository _versions;
		private readonly ILearningRecordService _learningRecords;
		private readonly IEventAggregator _events;

		public LmsSkillSheetService(ILmsSkillSheetsRepository sheets, ILmsSkillSheetVersionsRepository versions, ILearningRecordService learningRecords, IEventAggregator events)
		{
			_sheets = sheets;
			_versions = versions;
			_learningRecords = learningRecords;
			_events = events;
		}

		#region Reads

		public async Task<List<LmsSkillSheet>> GetSheetsAsync(int departmentId, bool includeDeleted = false) =>
			departmentId <= 0 ? new List<LmsSkillSheet>() : await _sheets.GetForDepartmentAsync(departmentId, includeDeleted) ?? new List<LmsSkillSheet>();

		public async Task<LmsSkillSheet> GetSheetAsync(int departmentId, int lmsSkillSheetId)
		{
			var sheet = departmentId <= 0 || lmsSkillSheetId <= 0 ? null : await _sheets.GetByIdForDepartmentAsync(departmentId, lmsSkillSheetId);
			return sheet != null && sheet.DepartmentId == departmentId ? sheet : null;
		}

		public async Task<List<LmsSkillSheetVersion>> GetVersionsAsync(int departmentId, int lmsSkillSheetId) =>
			(await _versions.GetForSheetAsync(departmentId, lmsSkillSheetId) ?? new List<LmsSkillSheetVersion>()).Where(v => v.DepartmentId == departmentId).ToList();

		public async Task<LmsSkillSheetVersion> GetVersionAsync(int departmentId, int lmsSkillSheetVersionId)
		{
			var version = lmsSkillSheetVersionId <= 0 ? null : await _versions.GetByIdForDepartmentAsync(departmentId, lmsSkillSheetVersionId);
			return version != null && version.DepartmentId == departmentId ? version : null;
		}

		public async Task<LmsSkillSheetVersion> GetPublishedVersionAsync(int departmentId, int lmsSkillSheetId)
		{
			var sheet = await GetSheetAsync(departmentId, lmsSkillSheetId);
			if (sheet?.CurrentVersionId == null)
				return null;
			var version = await GetVersionAsync(departmentId, sheet.CurrentVersionId.Value);
			return version != null && version.LmsSkillSheetId == sheet.LmsSkillSheetId && version.Status == (int)ProtocolVersionStatuses.Published ? version : null;
		}

		public async Task<LmsSkillSheetVersion> GetDraftAsync(int departmentId, int lmsSkillSheetId) =>
			(await GetVersionsAsync(departmentId, lmsSkillSheetId)).FirstOrDefault(v => v.Status == (int)ProtocolVersionStatuses.Draft);

		#endregion Reads

		#region Authoring

		private static void RequireAuthor(LmsActor actor)
		{
			if (actor == null || !actor.IsCurrentMember || !actor.MayManage)
				throw new InvalidOperationException("lms_skill_not_allowed");
			if (actor.Gate == null || !actor.Gate.CanAuthor)
				throw new InvalidOperationException("lms_skill_addon_required");
		}

		private async Task<LmsSkillSheet> RequireSheetAsync(LmsActor actor, int lmsSkillSheetId)
		{
			var sheet = await GetSheetAsync(actor.DepartmentId, lmsSkillSheetId);
			if (sheet == null || sheet.IsDeleted)
				throw new InvalidOperationException("lms_skill_sheet_not_found");
			return sheet;
		}

		private async Task ApplyInputAsync(LmsSkillSheet sheet, LmsSkillSheetInput input)
		{
			var title = Trim(input?.Title, LmsSkillSheet.TitleMaxLength);
			if (title == null)
				throw new InvalidOperationException("lms_skill_sheet_title_required");
			if (input.Kind != LmsSkillSheetKinds.SkillSheet && input.Kind != LmsSkillSheetKinds.ObservationReport)
				throw new InvalidOperationException("lms_skill_sheet_kind_invalid");
			if (input.CategoryId.HasValue && input.CategoryId.Value > 0 &&
				!(await _learningRecords.GetCategoriesAsync(sheet.DepartmentId) ?? new List<TrainingCategory>()).Any(c => c.TrainingCategoryId == input.CategoryId.Value && !c.IsDeleted))
				throw new InvalidOperationException("lms_skill_sheet_category_invalid");

			sheet.Title = title;
			sheet.Code = Trim(input.Code, LmsSkillSheet.CodeMaxLength);
			sheet.Description = Trim(input.Description, 4000);
			sheet.Kind = (int)input.Kind;
			sheet.CategoryId = input.CategoryId.HasValue && input.CategoryId.Value > 0 ? input.CategoryId : null;
			sheet.EvaluatorEligibilityJson = input.EvaluatorEligibility?.ToJson();
			sheet.NextSignerEligibilityJson = input.NextSignerEligibility?.ToJson();
			sheet.AllowSelfEvaluation = input.AllowSelfEvaluation;
			sheet.AllowCrewEvaluation = input.AllowCrewEvaluation;
		}

		public async Task<LmsSkillSheet> CreateAsync(LmsActor actor, LmsSkillSheetInput input, CancellationToken cancellationToken = default)
		{
			RequireAuthor(actor);
			var now = DateTime.UtcNow;
			var sheet = new LmsSkillSheet { DepartmentId = actor.DepartmentId, Status = (int)ProtocolStatuses.Draft, CreatedOn = now, CreatedByUserId = actor.UserId };
			await ApplyInputAsync(sheet, input);
			sheet = await _sheets.InsertAsync(sheet, cancellationToken, true);
			await _versions.InsertAsync(NewDraft(sheet, actor.UserId, now, new LmsSkillSheetDraftInput { Content = new LmsSkillSheetContent() }), cancellationToken, true);
			Audit(sheet.DepartmentId, actor.UserId, AuditLogTypes.LmsSkillSheetChanged, null, new { sheet.LmsSkillSheetId, sheet.Title, sheet.Code });
			return sheet;
		}

		public async Task<LmsSkillSheet> CreateFromTemplateAsync(LmsActor actor, string templateKey, CancellationToken cancellationToken = default)
		{
			RequireAuthor(actor);
			var template = LmsSkillTemplateCatalog.FindSheet(templateKey) ?? throw new InvalidOperationException("lms_skill_template_not_found");
			var now = DateTime.UtcNow;
			var sheet = await _sheets.InsertAsync(new LmsSkillSheet
			{
				DepartmentId = actor.DepartmentId,
				Title = template.Title,
				Code = template.Code,
				Description = template.Description,
				Kind = (int)template.Kind,
				Status = (int)ProtocolStatuses.Draft,
				AllowCrewEvaluation = template.AllowCrewEvaluation,
				TemplateKey = template.Key,
				CreatedOn = now,
				CreatedByUserId = actor.UserId
			}, cancellationToken, true);
			var content = new LmsSkillSheetContent { Steps = JsonConvert.DeserializeObject<List<LmsSkillStep>>(JsonConvert.SerializeObject(template.Steps)) };
			await _versions.InsertAsync(NewDraft(sheet, actor.UserId, now, new LmsSkillSheetDraftInput
			{
				Content = content, PassThreshold = template.PassThreshold, TimeLimitSeconds = template.TimeLimitSeconds, DefaultMinutes = template.DefaultMinutes
			}), cancellationToken, true);
			Audit(sheet.DepartmentId, actor.UserId, AuditLogTypes.LmsSkillSheetChanged, null, new { sheet.LmsSkillSheetId, sheet.Title, template = template.Key });
			return sheet;
		}

		private static LmsSkillSheetVersion NewDraft(LmsSkillSheet sheet, string userId, DateTime now, LmsSkillSheetDraftInput input)
		{
			var version = new LmsSkillSheetVersion
			{
				DepartmentId = sheet.DepartmentId,
				LmsSkillSheetId = sheet.LmsSkillSheetId,
				Status = (int)ProtocolVersionStatuses.Draft,
				CreatedOn = now,
				CreatedByUserId = userId
			};
			ApplyDraft(version, input);
			return version;
		}

		private static void ApplyDraft(LmsSkillSheetVersion version, LmsSkillSheetDraftInput input)
		{
			var content = input?.Content ?? new LmsSkillSheetContent();
			foreach (var step in content.Steps.Where(s => s != null))
			{
				step.Key = step.Key?.Trim();
				step.Label = step.Label?.Trim();
				step.Help = string.IsNullOrWhiteSpace(step.Help) ? null : step.Help.Trim();
				step.Options ??= new List<LmsSkillOption>();
				step.ComputeKeys ??= new List<string>();
			}
			content.Steps = content.Steps.Where(s => s != null).ToList();
			version.ContentJson = content.ToJson();
			version.PassThreshold = Math.Clamp(input?.PassThreshold ?? 100m, 0m, 100m);
			version.MaxAttempts = Math.Clamp(input?.MaxAttempts ?? 0, 0, 50);
			version.AttemptWindowDays = Math.Clamp(input?.AttemptWindowDays ?? 0, 0, 3650);
			version.TimeLimitSeconds = Math.Clamp(input?.TimeLimitSeconds ?? 0, 0, 24 * 3600);
			version.DefaultMinutes = Math.Clamp(input?.DefaultMinutes ?? 0, 0, 24 * 60);
			version.ContentHash = LmsSkillSheetEngine.VersionHash(content, version.PassThreshold, version.MaxAttempts, version.AttemptWindowDays, version.TimeLimitSeconds, version.DefaultMinutes);
		}

		public async Task<LmsSkillSheet> SaveSheetAsync(LmsActor actor, int lmsSkillSheetId, LmsSkillSheetInput input, CancellationToken cancellationToken = default)
		{
			RequireAuthor(actor);
			var sheet = await RequireSheetAsync(actor, lmsSkillSheetId);
			var before = new { sheet.Title, sheet.Code, sheet.Kind, sheet.CategoryId, sheet.EvaluatorEligibilityJson, sheet.NextSignerEligibilityJson, sheet.AllowSelfEvaluation, sheet.AllowCrewEvaluation };
			await ApplyInputAsync(sheet, input);
			sheet.UpdatedOn = DateTime.UtcNow;
			sheet.UpdatedByUserId = actor.UserId;
			if (!await _sheets.TryUpdateAsync(sheet, input.ExpectedRowVersion > 0 ? input.ExpectedRowVersion : sheet.RowVersion, cancellationToken))
				throw new InvalidOperationException("lms_skill_stale");
			Audit(sheet.DepartmentId, actor.UserId, AuditLogTypes.LmsSkillSheetChanged, before,
				new { sheet.Title, sheet.Code, sheet.Kind, sheet.CategoryId, sheet.EvaluatorEligibilityJson, sheet.NextSignerEligibilityJson, sheet.AllowSelfEvaluation, sheet.AllowCrewEvaluation });
			return sheet;
		}

		public async Task<LmsSkillSheetVersion> SaveDraftAsync(LmsActor actor, int lmsSkillSheetId, LmsSkillSheetDraftInput input, CancellationToken cancellationToken = default)
		{
			RequireAuthor(actor);
			var sheet = await RequireSheetAsync(actor, lmsSkillSheetId);
			var now = DateTime.UtcNow;
			var draft = await GetDraftAsync(actor.DepartmentId, sheet.LmsSkillSheetId);
			if (draft == null)
			{
				draft = NewDraft(sheet, actor.UserId, now, input);
				return await _versions.InsertAsync(draft, cancellationToken, true);
			}

			var expected = draft.RowVersion;
			ApplyDraft(draft, input);
			draft.UpdatedOn = now;
			draft.UpdatedByUserId = actor.UserId;
			if (!await _versions.TryUpdateAsync(draft, expected, cancellationToken))
				throw new InvalidOperationException("lms_skill_stale");
			return draft;
		}

		public async Task<LmsSkillSheetVersion> PublishAsync(LmsActor actor, int lmsSkillSheetId, bool major, string changeSummary, CancellationToken cancellationToken = default)
		{
			RequireAuthor(actor);
			var sheet = await RequireSheetAsync(actor, lmsSkillSheetId);
			var draft = await GetDraftAsync(actor.DepartmentId, sheet.LmsSkillSheetId) ?? throw new InvalidOperationException("lms_skill_sheet_no_draft");
			var errors = LmsSkillSheetEngine.Validate(LmsSkillSheetContent.Parse(draft.ContentJson));
			if (errors.Count > 0)
				throw new InvalidOperationException(errors[0].Split(':')[0]);
			var summary = Trim(changeSummary, 2000);
			var current = await GetPublishedVersionAsync(actor.DepartmentId, sheet.LmsSkillSheetId);
			if (current != null && summary == null)
				throw new InvalidOperationException("lms_skill_sheet_change_summary_required");

			var now = DateTime.UtcNow;
			if (current == null)
			{
				draft.Major = 1;
				draft.Minor = 0;
			}
			else
			{
				draft.Major = major ? current.Major + 1 : current.Major;
				draft.Minor = major ? 0 : current.Minor + 1;
				var currentExpected = current.RowVersion;
				current.Status = (int)ProtocolVersionStatuses.Superseded;
				current.SupersededOn = now;
				if (!await _versions.TryUpdateAsync(current, currentExpected, cancellationToken))
					throw new InvalidOperationException("lms_skill_stale");
			}

			var expected = draft.RowVersion;
			draft.Status = (int)ProtocolVersionStatuses.Published;
			draft.ChangeSummary = summary;
			draft.PublishedOn = now;
			draft.PublishedByUserId = actor.UserId;
			if (!await _versions.TryUpdateAsync(draft, expected, cancellationToken))
				throw new InvalidOperationException("lms_skill_stale");

			var sheetExpected = sheet.RowVersion;
			sheet.CurrentVersionId = draft.LmsSkillSheetVersionId;
			sheet.Status = (int)ProtocolStatuses.Published;
			sheet.UpdatedOn = now;
			sheet.UpdatedByUserId = actor.UserId;
			if (!await _sheets.TryUpdateAsync(sheet, sheetExpected, cancellationToken))
				throw new InvalidOperationException("lms_skill_stale");

			Audit(sheet.DepartmentId, actor.UserId, AuditLogTypes.LmsSkillSheetPublished, current == null ? null : new { current.LmsSkillSheetVersionId, current.Major, current.Minor },
				new { draft.LmsSkillSheetVersionId, draft.Major, draft.Minor, draft.ContentHash, draft.ChangeSummary });
			return draft;
		}

		public async Task<bool> DeleteAsync(LmsActor actor, int lmsSkillSheetId, CancellationToken cancellationToken = default)
		{
			RequireAuthor(actor);
			var sheet = await RequireSheetAsync(actor, lmsSkillSheetId);
			var expected = sheet.RowVersion;
			sheet.IsDeleted = true;
			sheet.Status = (int)ProtocolStatuses.Retired;
			sheet.UpdatedOn = DateTime.UtcNow;
			sheet.UpdatedByUserId = actor.UserId;
			if (!await _sheets.TryUpdateAsync(sheet, expected, cancellationToken))
				throw new InvalidOperationException("lms_skill_stale");
			Audit(sheet.DepartmentId, actor.UserId, AuditLogTypes.LmsSkillSheetChanged, new { sheet.LmsSkillSheetId, sheet.Title }, new { deleted = true });
			return true;
		}

		#endregion Authoring

		internal static string Trim(string value, int max)
		{
			var text = (value ?? string.Empty).Trim();
			return text.Length == 0 ? null : text.Length > max ? text.Substring(0, max) : text;
		}

		private void Audit(int departmentId, string userId, AuditLogTypes type, object before, object after) =>
			_events?.SendMessage(new AuditEvent
			{
				DepartmentId = departmentId,
				UserId = userId,
				Type = type,
				Before = before == null ? null : JsonConvert.SerializeObject(before),
				After = after == null ? null : JsonConvert.SerializeObject(after),
				Successful = true,
				ServerName = Environment.MachineName
			});
	}
}
