using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Config;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Lms;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services.Knowledge;

namespace Resgrid.Services
{
	/// <summary>
	/// LMS question explanations in the knowledge corpus (protocols-training-knowledge-plan §16.1 "Courses": question explanations, not
	/// answer keys; owner decision 2026-11-26). One item per department question bank; one chunk per live question with an explanation,
	/// holding the question and its explanation, never the options, the answer key or any attempt.
	/// <para>
	/// The rule against leaking answers: an explanation reveals the answer, so it is retrievable only by the people the bank editor already
	/// shows the answer keys to (<see cref="ILmsQuestionBankService.GetBanksAsync"/>: department administrators and Manage Courses holders
	/// for every bank; course editors for shared banks and their own private ones), behind the same LMS gate as courses. A learner never
	/// retrieves one from the assistant, whether or not they can still take the assessment; they see explanations only in their own attempt
	/// review, when the assessment's answer-review policy allows. Baseline training quizzes have no explanation field, so nothing of theirs
	/// is indexed. LMS banks carry no "exclude from AI" flag; a deleted bank, a retired question and the system publisher's banks (Academy
	/// checks, DepartmentId 0) are never indexed. Bank and question changes queue a reindex (<see cref="IKnowledgeIndexQueue"/>); the nightly
	/// repair pass catches every other writer.
	/// </para>
	/// </summary>
	public sealed class LmsQuestionExplanationKnowledgeSource : IKnowledgeCorpusSource
	{
		private static readonly KnowledgeSourceKinds[] SourceKinds = { KnowledgeSourceKinds.QuestionExplanation };

		private readonly ILmsAccessService _access;
		private readonly ILmsGateService _gate;
		private readonly ILmsQuestionBanksRepository _banks;
		private readonly ILmsQuestionsRepository _questions;
		private readonly ILmsQuestionBankService _bankService;

		public LmsQuestionExplanationKnowledgeSource(ILmsAccessService access, ILmsGateService gate, ILmsQuestionBanksRepository banks, ILmsQuestionsRepository questions,
			ILmsQuestionBankService bankService)
		{
			_access = access;
			_gate = gate;
			_banks = banks;
			_questions = questions;
			_bankService = bankService;
		}

		public IReadOnlyCollection<KnowledgeSourceKinds> Kinds => SourceKinds;

		/// <summary>A department bank that may be indexed at all: the department's own, not deleted.</summary>
		public static bool IsIndexable(LmsQuestionBank bank, int departmentId) =>
			bank != null && departmentId > 0 && bank.DepartmentId == departmentId && !bank.IsDeleted;

		/// <summary>A question whose explanation joins the corpus: live (not retired) and explained.</summary>
		public static bool IsIndexedQuestion(LmsQuestion question, LmsQuestionBank bank) =>
			question != null && bank != null && question.DepartmentId == bank.DepartmentId && question.LmsQuestionBankId == bank.LmsQuestionBankId &&
			!question.IsRetired && !string.IsNullOrWhiteSpace(question.Explanation);

		private async Task<bool> FlagOnAsync(int departmentId)
		{
			try
			{
				return departmentId > 0 && await _access.IsFlagEnabledAsync(departmentId);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"The LMS flag could not be read for department {departmentId}.");
				return false;
			}
		}

		public async Task<IReadOnlyList<string>> ListItemIdsAsync(int departmentId, KnowledgeSourceKinds kind, CancellationToken cancellationToken = default)
		{
			if (kind != KnowledgeSourceKinds.QuestionExplanation || !await FlagOnAsync(departmentId))
				return Array.Empty<string>();
			return (await _banks.GetForDepartmentAsync(departmentId) ?? new List<LmsQuestionBank>())
				.Where(b => IsIndexable(b, departmentId)).Select(b => b.LmsQuestionBankId.ToString(CultureInfo.InvariantCulture)).ToList();
		}

		public async Task<KnowledgeItemContent> BuildAsync(int departmentId, KnowledgeSourceKinds kind, string itemId, CancellationToken cancellationToken = default)
		{
			if (kind != KnowledgeSourceKinds.QuestionExplanation || !int.TryParse(itemId, NumberStyles.None, CultureInfo.InvariantCulture, out var bankId) ||
				!await FlagOnAsync(departmentId))
				return null;

			var bank = await _banks.GetVisibleByIdAsync(departmentId, bankId);
			if (!IsIndexable(bank, departmentId) || bank.LmsQuestionBankId != bankId)
				return null;

			var questions = (await _questions.GetForBankAsync(departmentId, bankId) ?? new List<LmsQuestion>())
				.Where(q => IsIndexedQuestion(q, bank)).OrderBy(q => q.SortOrder).ThenBy(q => q.LmsQuestionId).ToList();
			return new KnowledgeItemContent
			{
				DepartmentId = departmentId,
				Kind = KnowledgeSourceKinds.QuestionExplanation,
				ItemId = itemId,
				Chunks = questions.SelectMany(q => Chunks(departmentId, bank, q)).ToList()
			};
		}

		/// <summary>One question's chunks: the question and its explanation as plain text. The options and the key are never read.</summary>
		public static IEnumerable<KnowledgeChunk> Chunks(int departmentId, LmsQuestionBank bank, LmsQuestion question)
		{
			var questionText = StringHelpers.HtmlToPlainText(question.Text ?? string.Empty).Trim();
			var explanation = StringHelpers.HtmlToPlainText(question.Explanation ?? string.Empty).Trim();
			if (explanation.Length == 0)
				yield break;

			foreach (var text in KnowledgeChunker.Cut(questionText.Length == 0 ? explanation : questionText + "\n" + explanation, KnowledgeConfig.MaxChunkChars))
			{
				yield return new KnowledgeChunk
				{
					DepartmentId = departmentId,
					SourceKind = (int)KnowledgeSourceKinds.QuestionExplanation,
					ItemId = bank.LmsQuestionBankId.ToString(CultureInfo.InvariantCulture),
					VersionId = VersionOf(question),
					Title = KnowledgeChunkFactory.Truncate(bank.Name, KnowledgeChunk.TitleMaxLength) ?? string.Empty,
					Anchor = KnowledgeLinks.QuestionAnchor(question.LmsQuestionId),
					HeadingPath = KnowledgeChunkFactory.Truncate(questionText, KnowledgeChunk.HeadingPathMaxLength),
					Text = text,
					// No audience: the live readable set (the banks the reader may open in the editor) decides.
					AccessFlags = (int)KnowledgeAccessFlags.AssigneesRead
				};
			}
		}

		/// <summary>Bank questions are not versioned: their last change stands in for the version.</summary>
		public static string VersionOf(LmsQuestion question) =>
			"r" + (question.UpdatedOn ?? question.CreatedOn).ToUniversalTime().ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);

		public async Task AddReadableAsync(KnowledgeReader reader, KnowledgeReaderScope scope, CancellationToken cancellationToken = default)
		{
			if (reader == null || scope == null || !reader.CanViewTrainings || scope.DepartmentId <= 0)
				return;
			try
			{
				var actor = await _gate.GetActorAsync(scope.DepartmentId, scope.UserId);
				if (!LmsCourseKnowledgeSource.GateOpen(actor))
					return;
				scope.Allow(KnowledgeSourceKinds.QuestionExplanation, (await ReadableBankIdsAsync(actor)).Select(id => id.ToString(CultureInfo.InvariantCulture)));
			}
			catch (Exception ex)
			{
				// Unknown access is no access.
				Logging.LogException(ex, "Question explanation knowledge access could not be read.");
			}
		}

		/// <summary>The banks whose answer keys the reader may already open in the bank editor; empty for every learner.</summary>
		public async Task<HashSet<int>> ReadableBankIdsAsync(LmsActor actor)
		{
			if (!LmsCourseKnowledgeSource.GateOpen(actor))
				return new HashSet<int>();
			return new HashSet<int>((await _bankService.GetBanksAsync(actor) ?? new List<LmsQuestionBank>())
				.Where(b => IsIndexable(b, actor.DepartmentId)).Select(b => b.LmsQuestionBankId));
		}
	}
}
