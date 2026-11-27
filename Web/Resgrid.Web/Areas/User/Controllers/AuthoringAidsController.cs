using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Areas.User.Models.AuthoringAids;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// The K2 drafting aids' endpoints (plan §16.4): whether the department has them (404 otherwise, so the free tier never sees the panel)
	/// and one draft per request. A draft is returned to the editor, never saved: the author accepts what they keep and the editor saves it.
	/// </summary>
	[Area("User")]
	[Authorize]
	public class AuthoringAidsController : SecureBaseController
	{
		private static readonly System.Text.Json.JsonSerializerOptions CamelCase = new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase };

		private readonly IAuthoringAidService _aids;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Knowledge.AuthoringAids> _localizer;

		public AuthoringAidsController(IAuthoringAidService aids, IStringLocalizer<Resgrid.Localization.Areas.User.Knowledge.AuthoringAids> localizer)
		{
			_aids = aids;
			_localizer = localizer;
		}

		private string L(string key) => _localizer[key].Value;

		[HttpGet]
		public async Task<IActionResult> Status(CancellationToken cancellationToken)
		{
			var status = await _aids.GetStatusAsync(DepartmentId, UserId, cancellationToken);
			if (status == null || status.Reason == AuthoringAidOutcomes.NotEntitled)
				return NotFound();
			return Json(new { available = status.Available, message = status.Available ? null : Message(status.Reason) });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[RequestSizeLimit(600_000)]
		public async Task<IActionResult> Draft([FromForm] AuthoringAidInput input, CancellationToken cancellationToken)
		{
			input ??= new AuthoringAidInput();
			var draft = await _aids.DraftAsync(new AuthoringAidRequest
			{
				DepartmentId = DepartmentId,
				UserId = UserId,
				Locale = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName,
				Kind = input.Kind,
				SourceKind = input.SourceKind,
				SourceId = input.SourceId,
				ContentTitle = input.ContentTitle,
				ContentHtml = input.ContentHtml,
				ExcludeFromAi = input.ExcludeFromAi,
				QuestionCount = input.QuestionCount
			}, cancellationToken) ?? new AuthoringAidDraft { Outcome = AuthoringAidOutcomes.Unavailable };

			if (draft.Outcome == AuthoringAidOutcomes.NotEntitled)
				return NotFound();

			var view = new AuthoringAidDraftView
			{
				Outcome = draft.Outcome,
				Kind = draft.Kind,
				ExchangeId = draft.ExchangeId,
				Questions = draft.Questions,
				Outline = draft.Outline,
				Changes = draft.Changes,
				Message = draft.Outcome == AuthoringAidOutcomes.Drafted ? L("DraftedNotice") : Message(draft.Outcome),
				Notice = draft.Dropped > 0 ? string.Format(L("DroppedNotice"), draft.Dropped) : null
			};
			return Json(view, CamelCase);
		}

		private string Message(string outcome) => outcome switch
		{
			AuthoringAidOutcomes.NotAllowed => L("NotAllowedNotice"),
			AuthoringAidOutcomes.NotFound => L("NotFoundNotice"),
			AuthoringAidOutcomes.Excluded => L("ExcludedNotice"),
			AuthoringAidOutcomes.KeptLocal => L("KeptLocalNotice"),
			AuthoringAidOutcomes.NoChanges => L("NoChangesNotice"),
			AuthoringAidOutcomes.NoContent => L("NoContentNotice"),
			AuthoringAidOutcomes.Busy => L("BusyNotice"),
			AuthoringAidOutcomes.BudgetExhausted => L("BudgetNotice"),
			AuthoringAidOutcomes.Refused => L("RefusedNotice"),
			AuthoringAidOutcomes.Invalid => L("InvalidNotice"),
			_ => L("UnavailableNotice")
		};
	}
}
