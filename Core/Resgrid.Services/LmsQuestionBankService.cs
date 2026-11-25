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
	/// Question banks (T2 item 4, §13a): pools with tags, images, explanations and per-question points; department-shared banks any
	/// course author may draw from, private banks only their owner and Manage Courses holders use. Questions are retired, never
	/// deleted, because past attempts drew them (their own snapshots stand, R-T6). Needs the LMS add-on to change (rule 3).
	/// </summary>
	public class LmsQuestionBankService : ILmsQuestionBankService
	{
		public const int MaxOptions = 10;

		private readonly ILmsQuestionBanksRepository _banks;
		private readonly ILmsQuestionsRepository _questions;
		private readonly ILmsCourseOwnersRepository _owners;
		private readonly IEventAggregator _eventAggregator;

		public LmsQuestionBankService(ILmsQuestionBanksRepository banks, ILmsQuestionsRepository questions, ILmsCourseOwnersRepository owners, IEventAggregator eventAggregator)
		{
			_banks = banks;
			_questions = questions;
			_owners = owners;
			_eventAggregator = eventAggregator;
		}

		/// <summary>Whether the actor may draw from (and edit) the bank: a shared bank, their own, or any for Manage Courses.</summary>
		public static bool MayUse(LmsActor actor, LmsQuestionBank bank) =>
			actor != null && bank != null && actor.IsCurrentMember && bank.DepartmentId == actor.DepartmentId && !bank.IsDeleted &&
			(actor.MayManage || bank.IsShared || string.Equals(bank.OwnerUserId, actor.UserId, StringComparison.OrdinalIgnoreCase));

		/// <summary>Course editors (owners with the edit right) and Manage Courses holders author banks.</summary>
		private async Task<bool> IsAuthorAsync(LmsActor actor)
		{
			if (actor == null || !actor.IsCurrentMember)
				return false;
			if (actor.MayManage)
				return true;
			return (await _owners.GetForUserAsync(actor.DepartmentId, actor.UserId) ?? new List<LmsCourseOwner>()).Any(o => o.CanEdit);
		}

		public async Task<List<LmsQuestionBank>> GetBanksAsync(LmsActor actor)
		{
			if (!await IsAuthorAsync(actor))
				return new List<LmsQuestionBank>();
			return (await _banks.GetForDepartmentAsync(actor.DepartmentId) ?? new List<LmsQuestionBank>()).Where(b => MayUse(actor, b)).ToList();
		}

		public async Task<LmsQuestionBank> GetBankAsync(LmsActor actor, int lmsQuestionBankId)
		{
			var bank = await _banks.GetVisibleByIdAsync(actor?.DepartmentId ?? 0, lmsQuestionBankId);
			return MayUse(actor, bank) && await IsAuthorAsync(actor) ? bank : null;
		}

		public async Task<List<LmsQuestion>> GetQuestionsAsync(LmsActor actor, int lmsQuestionBankId)
		{
			var bank = await GetBankAsync(actor, lmsQuestionBankId);
			return bank == null ? new List<LmsQuestion>() : await _questions.GetForBankAsync(actor.DepartmentId, bank.LmsQuestionBankId) ?? new List<LmsQuestion>();
		}

		public async Task<LmsResult> SaveBankAsync(LmsActor actor, LmsQuestionBank bank, CancellationToken cancellationToken = default)
		{
			if (!await IsAuthorAsync(actor))
				return LmsResult.Fail(LmsErrors.NotAuthorized);
			if (!LmsLapseRules.CanAssignNew(actor.Gate, false))
				return LmsResult.Fail(LmsErrors.AddonRequired);
			if (bank == null || string.IsNullOrWhiteSpace(bank.Name))
				return LmsResult.Fail(LmsErrors.InvalidInput);

			LmsQuestionBank stored;
			if (bank.LmsQuestionBankId > 0)
			{
				stored = await GetBankAsync(actor, bank.LmsQuestionBankId);
				if (stored == null)
					return LmsResult.Fail(LmsErrors.NotFound);
				// Only the owner (or Manage Courses) changes who may use a private bank.
				if (!actor.MayManage && !string.Equals(stored.OwnerUserId, actor.UserId, StringComparison.OrdinalIgnoreCase))
					return LmsResult.Fail(LmsErrors.NotAuthorized);
			}
			else
			{
				stored = new LmsQuestionBank { DepartmentId = actor.DepartmentId, OwnerUserId = actor.UserId, CreatedOn = DateTime.UtcNow, CreatedByUserId = actor.UserId };
			}

			stored.Name = LmsCourseService.Truncate(bank.Name.Trim(), 250);
			stored.Description = LmsCourseService.Truncate(bank.Description?.Trim(), 2000);
			stored.IsShared = bank.IsShared;
			stored.Tags = LmsCourseService.NormalizeTags(bank.Tags);
			await _banks.SaveOrUpdateAsync(stored, cancellationToken, true);
			Audit(actor, JsonConvert.SerializeObject(new { stored.LmsQuestionBankId, stored.Name, stored.IsShared }));
			return new LmsResult { Value = stored };
		}

		public async Task<LmsResult> DeleteBankAsync(LmsActor actor, int lmsQuestionBankId, CancellationToken cancellationToken = default)
		{
			var bank = await GetBankAsync(actor, lmsQuestionBankId);
			if (bank == null)
				return LmsResult.Fail(LmsErrors.NotFound);
			if (!actor.MayManage && !string.Equals(bank.OwnerUserId, actor.UserId, StringComparison.OrdinalIgnoreCase))
				return LmsResult.Fail(LmsErrors.NotAuthorized);
			if (!LmsLapseRules.CanAssignNew(actor.Gate, false))
				return LmsResult.Fail(LmsErrors.AddonRequired);

			// Soft delete: lessons and attempts keep the id; a deleted bank is no longer offered.
			bank.IsDeleted = true;
			await _banks.UpdateAsync(bank, cancellationToken, true);
			Audit(actor, JsonConvert.SerializeObject(new { bank.LmsQuestionBankId, deleted = true }));
			return new LmsResult { Value = bank };
		}

		public async Task<LmsResult> SaveQuestionAsync(LmsActor actor, LmsQuestion question, CancellationToken cancellationToken = default)
		{
			if (question == null)
				return LmsResult.Fail(LmsErrors.InvalidInput);
			var bank = await GetBankAsync(actor, question.LmsQuestionBankId);
			if (bank == null)
				return LmsResult.Fail(LmsErrors.NotFound);
			if (!LmsLapseRules.CanAssignNew(actor.Gate, false))
				return LmsResult.Fail(LmsErrors.AddonRequired);

			var error = Validate(question);
			if (error != LmsErrors.None)
				return LmsResult.Fail(error);

			LmsQuestion stored;
			var now = DateTime.UtcNow;
			if (question.LmsQuestionId > 0)
			{
				stored = await _questions.GetVisibleByIdAsync(actor.DepartmentId, question.LmsQuestionId);
				if (stored == null || stored.LmsQuestionBankId != bank.LmsQuestionBankId || stored.DepartmentId != actor.DepartmentId)
					return LmsResult.Fail(LmsErrors.NotFound);
				stored.UpdatedOn = now;
			}
			else
			{
				var existing = await _questions.GetForBankAsync(actor.DepartmentId, bank.LmsQuestionBankId) ?? new List<LmsQuestion>();
				stored = new LmsQuestion
				{
					DepartmentId = actor.DepartmentId,
					LmsQuestionBankId = bank.LmsQuestionBankId,
					SortOrder = existing.Select(q => q.SortOrder).DefaultIfEmpty(0).Max() + 1,
					CreatedOn = now,
					CreatedByUserId = actor.UserId
				};
			}

			stored.Kind = question.Kind;
			stored.Text = LmsCourseService.Truncate(question.Text.Trim(), 4000);
			stored.ImageUrl = string.IsNullOrWhiteSpace(question.ImageUrl) ? null : question.ImageUrl.Trim();
			stored.Explanation = LmsCourseService.Truncate(question.Explanation?.Trim(), 4000);
			stored.Tags = LmsCourseService.NormalizeTags(question.Tags);
			stored.Points = question.Points <= 0 ? 1 : Math.Min(100, question.Points);
			stored.Options = NormalizeOptions(question);
			await _questions.SaveOrUpdateAsync(stored, cancellationToken, true);
			return new LmsResult { Value = stored };
		}

		public async Task<LmsResult> RetireQuestionAsync(LmsActor actor, int lmsQuestionId, CancellationToken cancellationToken = default)
		{
			var question = await _questions.GetVisibleByIdAsync(actor?.DepartmentId ?? 0, lmsQuestionId);
			if (question == null || question.DepartmentId != actor.DepartmentId || await GetBankAsync(actor, question.LmsQuestionBankId) == null)
				return LmsResult.Fail(LmsErrors.NotFound);
			if (!LmsLapseRules.CanAssignNew(actor.Gate, false))
				return LmsResult.Fail(LmsErrors.AddonRequired);
			question.IsRetired = true;
			question.UpdatedOn = DateTime.UtcNow;
			await _questions.UpdateAsync(question, cancellationToken, true);
			return new LmsResult { Value = question };
		}

		/// <summary>The per-kind rules: one correct option (single, true/false), at least one (multi), two or more to order.</summary>
		public static LmsErrors Validate(LmsQuestion question)
		{
			if (question == null || string.IsNullOrWhiteSpace(question.Text) || !Enum.IsDefined(typeof(LmsQuestionKinds), question.Kind))
				return LmsErrors.InvalidInput;
			if (!string.IsNullOrWhiteSpace(question.ImageUrl) && (!Uri.TryCreate(question.ImageUrl.Trim(), UriKind.Absolute, out var image) || image.Scheme != Uri.UriSchemeHttps))
				return LmsErrors.InvalidInput;

			var options = (question.Options ?? new List<LmsQuestionOption>()).Where(o => o != null && !string.IsNullOrWhiteSpace(o.Text)).ToList();
			switch ((LmsQuestionKinds)question.Kind)
			{
				case LmsQuestionKinds.SingleChoice:
					return options.Count >= 2 && options.Count <= MaxOptions && options.Count(o => o.IsCorrect) == 1 ? LmsErrors.None : LmsErrors.InvalidInput;
				case LmsQuestionKinds.MultiChoice:
					return options.Count >= 2 && options.Count <= MaxOptions && options.Any(o => o.IsCorrect) ? LmsErrors.None : LmsErrors.InvalidInput;
				case LmsQuestionKinds.TrueFalse:
					return options.Count == 2 && options.Count(o => o.IsCorrect) == 1 ? LmsErrors.None : LmsErrors.InvalidInput;
				case LmsQuestionKinds.Ordering:
					return options.Count >= 2 && options.Count <= MaxOptions ? LmsErrors.None : LmsErrors.InvalidInput;
				case LmsQuestionKinds.ShortAnswer:
					return LmsErrors.None;
				default:
					return LmsErrors.InvalidInput;
			}
		}

		/// <summary>Options with stable keys a, b, c… in the order given (for Ordering, the correct order).</summary>
		public static List<LmsQuestionOption> NormalizeOptions(LmsQuestion question)
		{
			if (question.Kind == (int)LmsQuestionKinds.ShortAnswer)
				return new List<LmsQuestionOption>();
			var options = (question.Options ?? new List<LmsQuestionOption>()).Where(o => o != null && !string.IsNullOrWhiteSpace(o.Text)).Take(MaxOptions).ToList();
			if (question.Kind == (int)LmsQuestionKinds.TrueFalse)
				return options.Select((o, i) => new LmsQuestionOption { Key = i == 0 ? "t" : "f", Text = LmsCourseService.Truncate(o.Text.Trim(), 500), IsCorrect = o.IsCorrect }).ToList();
			return options.Select((o, i) => new LmsQuestionOption
			{
				Key = ((char)('a' + i)).ToString(),
				Text = LmsCourseService.Truncate(o.Text.Trim(), 500),
				IsCorrect = question.Kind != (int)LmsQuestionKinds.Ordering && o.IsCorrect
			}).ToList();
		}

		private void Audit(LmsActor actor, string after)
		{
			_eventAggregator?.SendMessage<AuditEvent>(new AuditEvent
			{
				DepartmentId = actor.DepartmentId,
				UserId = actor.UserId,
				Type = AuditLogTypes.LmsQuestionBankChanged,
				After = after,
				Successful = true,
				ServerName = Environment.MachineName
			});
		}
	}
}
