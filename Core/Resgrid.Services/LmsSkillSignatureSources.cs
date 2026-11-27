using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Lms;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// The SkillEvaluation signature subject (S3 evaluator mode, registered by T4 as round-4D left it): "evaluation:{id}" is a skill
	/// evaluation (the evaluator, then the next signer the evaluator picked), "task-book:{id}" a position task book's certification (the
	/// final evaluator recommends, the certifying official certifies). Describe hands the signing layer the hash and evidence document; the
	/// completed request writes back once (both write-backs are idempotent, OnSignedAsync runs per signed step). The services come in lazily:
	/// they create the requests through the signing layer, which holds this source.
	/// </summary>
	public class LmsSkillSignatureSource : ISignatureSubjectSource
	{
		private readonly Lazy<ILmsSkillEvaluationService> _evaluations;
		private readonly Lazy<ILmsTaskBookService> _taskBooks;

		public LmsSkillSignatureSource(Lazy<ILmsSkillEvaluationService> evaluations, Lazy<ILmsTaskBookService> taskBooks)
		{
			_evaluations = evaluations;
			_taskBooks = taskBooks;
		}

		public SignatureSubjectKinds Kind => SignatureSubjectKinds.SkillEvaluation;

		public async Task<SignatureSubjectInfo> DescribeAsync(int departmentId, string subjectId, string userId)
		{
			var (kind, id) = LmsSkillRules.ParseSubject(subjectId);
			if (departmentId <= 0 || id <= 0)
				return null;
			return kind switch
			{
				"evaluation" => await _evaluations.Value.DescribeForSignatureAsync(departmentId, id),
				"task-book" => await _taskBooks.Value.DescribeForSignatureAsync(departmentId, id),
				_ => null
			};
		}

		public async Task OnSignedAsync(SignatureRequestRecord request, SignatureRequestSigner signer, string actingUserId, CancellationToken cancellationToken = default)
		{
			if (request == null || request.Status != (int)SignatureRequestStatuses.Completed)
				return;
			var (kind, _) = LmsSkillRules.ParseSubject(request.SubjectId);
			if (kind == "evaluation")
				await _evaluations.Value.CompleteFromSignatureAsync(request, cancellationToken);
			else if (kind == "task-book")
				await _taskBooks.Value.CertifyFromSignatureAsync(request, cancellationToken);
		}
	}

	/// <summary>
	/// The Training module's access rule for SkillEvaluation requests on the signing pages (detail, resend, withdraw, hand device): LMS
	/// managers (Manage Courses or department admins), the evaluation's evaluator or requester, and for a task book whoever initiated it.
	/// </summary>
	public class LmsSkillSignatureAccessRule : ISignatureSubjectAccessRule
	{
		private readonly ILmsGateService _gate;
		private readonly ILmsSkillEvaluationsRepository _evaluations;
		private readonly ILmsTaskBooksRepository _books;

		public LmsSkillSignatureAccessRule(ILmsGateService gate, ILmsSkillEvaluationsRepository evaluations, ILmsTaskBooksRepository books)
		{
			_gate = gate;
			_evaluations = evaluations;
			_books = books;
		}

		public SignatureSubjectKinds Kind => SignatureSubjectKinds.SkillEvaluation;

		public async Task<bool> CanActOnAsync(int departmentId, string userId, string subjectId)
		{
			var (kind, id) = LmsSkillRules.ParseSubject(subjectId);
			if (departmentId <= 0 || id <= 0 || string.IsNullOrWhiteSpace(userId))
				return false;
			try
			{
				var actor = await _gate.GetActorAsync(departmentId, userId);
				if (actor == null || !actor.IsCurrentMember || !actor.Gate.FlagOn)
					return false;
				if (kind == "evaluation")
				{
					var evaluation = await _evaluations.GetByIdForDepartmentAsync(departmentId, id);
					return evaluation != null && evaluation.DepartmentId == departmentId &&
						(actor.MayManage || LmsSkillRules.SameUser(evaluation.EvaluatorUserId, userId) || LmsSkillRules.SameUser(evaluation.CreatedByUserId, userId));
				}
				if (kind == "task-book")
				{
					var book = await _books.GetByIdForDepartmentAsync(departmentId, id);
					return book != null && book.DepartmentId == departmentId && (actor.MayManage || LmsSkillRules.SameUser(book.InitiatedByUserId, userId));
				}
				return false;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Skill evaluation signature access could not be checked.");
				return false;
			}
		}
	}

	/// <summary>Pass/fail by skill sheet or member (§13a "rating and pass/fail reports", LMS).</summary>
	public class LmsSkillPassFailReportSource : ILmsReportSource
	{
		private readonly ILmsSkillEvaluationsRepository _evaluations;
		private readonly ILmsSkillEvaluationMembersRepository _members;
		private readonly IDepartmentsService _departments;

		public LmsSkillPassFailReportSource(ILmsSkillEvaluationsRepository evaluations, ILmsSkillEvaluationMembersRepository members, IDepartmentsService departments)
		{
			_evaluations = evaluations;
			_members = members;
			_departments = departments;
		}

		public LmsReportDescriptor Descriptor { get; } = new LmsReportDescriptor
		{
			Key = "skill-pass-fail", NameKey = "Skl.Report.PassFail", DescriptionKey = "Skl.Report.PassFailHelp", Tier = LmsReportTiers.Lms, UsesPeriod = true,
			Groupings = new Dictionary<int, string> { { 0, "Skl.GroupBy.Sheet" }, { 1, "Skl.GroupBy.Member" } }
		};

		public async Task<LmsReportTable> BuildAsync(LmsReportRequest request, Func<string, string> text)
		{
			var evaluations = (await _evaluations.GetCompletedForReportAsync(request.DepartmentId, request.FromUtc, request.ToUtc, null) ?? new List<LmsSkillEvaluation>())
				.Where(e => e.DepartmentId == request.DepartmentId).ToList();
			var byMember = request.Parameters.GroupBy == 1;
			var table = new LmsReportTable
			{
				Title = text(Descriptor.NameKey),
				Columns = { text(byMember ? "Skl.Col.Member" : "Skl.Col.Sheet"), text("Skl.Col.Evaluations"), text("Skl.Col.Passed"), text("Skl.Col.Failed"),
					text("Skl.Col.PassRate"), text("Skl.Col.Critical"), text("Skl.Col.AverageScore") }
			};

			IEnumerable<IGrouping<string, LmsSkillEvaluation>> groups;
			if (byMember)
			{
				var members = (await _members.GetForEvaluationsAsync(request.DepartmentId, evaluations.Select(e => e.LmsSkillEvaluationId)) ?? new List<LmsSkillEvaluationMember>())
					.Where(m => m.IsIncluded).ToList();
				var active = request.Parameters.IncludeFormerMembers ? null
					: new HashSet<string>(await _departments.GetActiveMemberUserIdsAsync(request.DepartmentId) ?? new HashSet<string>(), StringComparer.OrdinalIgnoreCase);
				var names = LmsSkillEvaluationService.Names(await _departments.GetAllPersonnelNamesForDepartmentAsync(request.DepartmentId));
				var index = evaluations.ToDictionary(e => e.LmsSkillEvaluationId);
				groups = members.Where(m => active == null || active.Contains(m.UserId)).Where(m => index.ContainsKey(m.LmsSkillEvaluationId))
					.Select(m => (Name: LmsSkillEvaluationService.Name(names, m.UserId), Evaluation: index[m.LmsSkillEvaluationId]))
					.GroupBy(x => x.Name, x => x.Evaluation, StringComparer.CurrentCultureIgnoreCase);
			}
			else
				groups = evaluations.GroupBy(e => string.IsNullOrWhiteSpace(e.Code) ? e.Title : e.Title + " (" + e.Code + ")", StringComparer.CurrentCultureIgnoreCase);

			foreach (var group in groups.OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase))
			{
				var list = group.ToList();
				var passed = list.Count(e => e.Result == (int)LmsSkillEvaluationResults.Passed);
				var scores = list.Where(e => e.Score.HasValue).Select(e => e.Score.Value).ToList();
				table.Rows.Add(new List<string>
				{
					group.Key, list.Count.ToString(request.Culture), passed.ToString(request.Culture), (list.Count - passed).ToString(request.Culture),
					list.Count == 0 ? string.Empty : (100m * passed / list.Count).ToString("0.#", request.Culture) + "%",
					list.Count(e => e.CriticalFailed).ToString(request.Culture),
					scores.Count == 0 ? string.Empty : scores.Average().ToString("0.#", request.Culture)
				});
			}
			table.Footer.Add(text("Skl.Col.Evaluations") + ": " + evaluations.Count.ToString(request.Culture));
			return table;
		}
	}

	/// <summary>Ratings by question over time (§13a "colour-scale ratings ... ratings by question over time", LMS): rating-scale steps per month.</summary>
	public class LmsSkillRatingsReportSource : ILmsReportSource
	{
		private readonly ILmsSkillEvaluationsRepository _evaluations;
		private readonly ILmsSkillSheetVersionsRepository _versions;

		public LmsSkillRatingsReportSource(ILmsSkillEvaluationsRepository evaluations, ILmsSkillSheetVersionsRepository versions)
		{
			_evaluations = evaluations;
			_versions = versions;
		}

		public LmsReportDescriptor Descriptor { get; } = new LmsReportDescriptor
		{
			Key = "skill-ratings", NameKey = "Skl.Report.Ratings", DescriptionKey = "Skl.Report.RatingsHelp", Tier = LmsReportTiers.Lms, UsesPeriod = true
		};

		public async Task<LmsReportTable> BuildAsync(LmsReportRequest request, Func<string, string> text)
		{
			var evaluations = (await _evaluations.GetCompletedForReportAsync(request.DepartmentId, request.FromUtc, request.ToUtc, null) ?? new List<LmsSkillEvaluation>())
				.Where(e => e.DepartmentId == request.DepartmentId && e.LmsSkillSheetVersionId.HasValue).ToList();
			var contents = new Dictionary<int, LmsSkillSheetContent>();
			foreach (var versionId in evaluations.Select(e => e.LmsSkillSheetVersionId.Value).Distinct())
			{
				var version = await _versions.GetByIdForDepartmentAsync(request.DepartmentId, versionId);
				if (version != null)
					contents[versionId] = LmsSkillSheetContent.Parse(version.ContentJson);
			}

			var cells = new List<(string Sheet, string Step, string Month, int Rating)>();
			foreach (var evaluation in evaluations)
			{
				if (!contents.TryGetValue(evaluation.LmsSkillSheetVersionId.Value, out var content))
					continue;
				var answers = LmsSkillAnswers.Parse(evaluation.AnswersJson);
				var month = (evaluation.CompletedOn ?? evaluation.StartedOn).ToString("yyyy-MM", CultureInfo.InvariantCulture);
				foreach (var step in content.Steps.Where(s => s != null && s.StepType == LmsSkillStepTypes.RatingScale))
				{
					var answer = answers.Get(step.Key);
					if (answer?.Status == (int)LmsSkillAnswerStatuses.Answered && int.TryParse(answer.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rating))
						cells.Add((evaluation.Title, step.Label, month, rating));
				}
			}

			var table = new LmsReportTable
			{
				Title = text(Descriptor.NameKey),
				Columns = { text("Skl.Col.Sheet"), text("Skl.Col.Step"), text("Skl.Col.Month"), text("Skl.Col.Ratings"), text("Skl.Col.AverageRating"), text("Skl.Col.Lowest"), text("Skl.Col.Highest") }
			};
			foreach (var group in cells.GroupBy(c => (c.Sheet, c.Step, c.Month)).OrderBy(g => g.Key.Sheet, StringComparer.CurrentCultureIgnoreCase)
				         .ThenBy(g => g.Key.Step, StringComparer.CurrentCultureIgnoreCase).ThenBy(g => g.Key.Month, StringComparer.Ordinal))
			{
				var ratings = group.Select(c => c.Rating).ToList();
				table.Rows.Add(new List<string>
				{
					group.Key.Sheet, group.Key.Step, group.Key.Month, ratings.Count.ToString(request.Culture), ratings.Average().ToString("0.##", request.Culture),
					ratings.Min().ToString(request.Culture), ratings.Max().ToString(request.Culture)
				});
			}
			return table;
		}
	}
}
