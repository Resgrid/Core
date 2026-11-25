using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models.Knowledge;
using Resgrid.Web.Helpers;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// Scenario practice on a protocol (K3 tutor, the "Practice" panel of the protocol view): status, a new scenario from the version in
	/// force, and the member's answers scored against the cited sections. Practice only: nothing is recorded on the transcript. Behind the
	/// tutor's gate (Enhanced AI with Ai.Knowledge and the LMS add-on); 404 otherwise.
	/// </summary>
	[Area("User")]
	[Authorize]
	public class KnowledgePracticeController : SecureBaseController
	{
		private static readonly System.Text.Json.JsonSerializerOptions CamelCase = new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase };

		private readonly IKnowledgePracticeService _practice;
		private readonly IDepartmentsService _departments;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Knowledge.Knowledge> _localizer;

		public KnowledgePracticeController(IKnowledgePracticeService practice, IDepartmentsService departments,
			IStringLocalizer<Resgrid.Localization.Areas.User.Knowledge.Knowledge> localizer)
		{
			_practice = practice;
			_departments = departments;
			_localizer = localizer;
		}

		private string L(string key) => _localizer[key].Value;

		private KnowledgeReader Reader() => new KnowledgeReader
		{
			DepartmentId = DepartmentId,
			UserId = UserId,
			Locale = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName,
			CanViewProtocols = User.HasClaim(ResgridClaimTypes.Resources.Protocols, ResgridClaimTypes.Actions.View),
			CanViewTrainings = User.HasClaim(ResgridClaimTypes.Resources.Training, ResgridClaimTypes.Actions.View),
			CanViewDocuments = User.HasClaim(ResgridClaimTypes.Resources.Documents, ResgridClaimTypes.Actions.View)
		};

		[HttpGet]
		public async Task<IActionResult> Status(CancellationToken cancellationToken)
		{
			var status = await _practice.GetStatusAsync(Reader(), cancellationToken) ?? new KnowledgePracticeStatus();
			return Json(new { available = status.Available, keptLocal = status.Reason == KnowledgePracticeOutcomes.KeptLocal });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Start([FromForm] int protocolId, CancellationToken cancellationToken)
		{
			var scenario = await _practice.StartAsync(Reader(), protocolId, cancellationToken);
			if (scenario == null || scenario.Outcome == KnowledgeOutcomes.NotEntitled)
				return NotFound();

			var department = await _departments.GetDepartmentByIdAsync(DepartmentId, false);
			return Json(new KnowledgePracticeScenarioView
			{
				Outcome = scenario.Outcome,
				Message = scenario.Outcome == KnowledgePracticeOutcomes.Ready ? null : MessageFor(scenario.Outcome),
				ExchangeId = scenario.ExchangeId,
				Scenario = scenario.Scenario,
				Version = string.IsNullOrWhiteSpace(scenario.VersionLabel) ? null : string.Format(L("VersionLabel"), scenario.VersionLabel),
				Questions = scenario.Questions.Select(q => new KnowledgePracticeQuestionView { Key = q.Key, Text = q.Text, Refs = q.Refs }).ToList(),
				Citations = scenario.Citations.Select(c => Citation(c, department)).ToList()
			}, CamelCase);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Score([FromForm] string exchangeId, [FromForm] List<string> keys, [FromForm] List<string> answers, CancellationToken cancellationToken)
		{
			keys ??= new List<string>();
			answers ??= new List<string>();
			var given = keys.Take(8).Select((k, i) => new KnowledgePracticeAnswer { Key = k, Text = i < answers.Count ? answers[i] : null }).ToList();
			var result = await _practice.ScoreAsync(Reader(), exchangeId, given, cancellationToken);
			if (result == null || result.Outcome == KnowledgeOutcomes.NotEntitled)
				return NotFound();

			var department = await _departments.GetDepartmentByIdAsync(DepartmentId, false);
			var view = new KnowledgePracticeResultView
			{
				Outcome = result.Outcome,
				Message = result.Outcome == KnowledgePracticeOutcomes.Scored ? string.Format(L("PracticeScoreFormat"), result.Total, result.Max) : MessageFor(result.Outcome),
				Items = result.Items.Select(i => new KnowledgePracticeItemView
				{
					Key = i.Key,
					Score = i.Score.HasValue ? string.Format(L("PracticeItemScoreFormat"), i.Score.Value, KnowledgePracticeItemScore.MaxScore) : L("PracticeNotScored"),
					Feedback = i.Feedback,
					Refs = i.Refs
				}).ToList(),
				Citations = result.Citations.Select(c => Citation(c, department)).ToList(),
				DosingToolUrl = result.DosingToolUrl
			};
			if (result.QuotesDose)
				view.Notices.Add(L("DoseNotice"));
			return Json(view, CamelCase);
		}

		private string MessageFor(string outcome) => outcome switch
		{
			KnowledgePracticeOutcomes.Excluded => L("PracticeExcluded"),
			KnowledgePracticeOutcomes.KeptLocal => L("PracticeKeptLocal"),
			KnowledgePracticeOutcomes.NoContent => L("PracticeNoContent"),
			KnowledgePracticeOutcomes.Refused => L("PracticeRefused"),
			KnowledgePracticeOutcomes.Stale => L("PracticeStale"),
			KnowledgePracticeOutcomes.NotFound => L("PracticeStale"),
			KnowledgeOutcomes.BudgetExhausted => L("BudgetNotice"),
			KnowledgeOutcomes.Busy => L("BusyNotice"),
			KnowledgeOutcomes.Invalid => L("PracticeAnswerRequired"),
			_ => L("UnavailableNotice")
		};

		private KnowledgeCitationView Citation(KnowledgeCitation citation, Department department) => new KnowledgeCitationView
		{
			Ref = citation.Ref,
			Title = citation.Title,
			Section = citation.HeadingPath,
			Version = string.IsNullOrWhiteSpace(citation.VersionLabel) ? null : string.Format(L("VersionLabel"), citation.VersionLabel),
			Effective = citation.EffectiveOn.HasValue && department != null
				? string.Format(L("EffectiveLabel"), citation.EffectiveOn.Value.TimeConverter(department).ToString("d", CultureInfo.CurrentUICulture))
				: null,
			Url = KnowledgeLinks.WebPath(citation.SourceKind, citation.ItemId, citation.VersionId, citation.Anchor, Config.SystemBehaviorConfig.AcademyUrl),
			Excerpt = citation.Excerpt,
			Kind = L("KindProtocol")
		};
	}
}
