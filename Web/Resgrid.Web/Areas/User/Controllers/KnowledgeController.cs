using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Helpers;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models.Knowledge;
using Resgrid.Web.Helpers;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// Department knowledge (K1): the "Ask" panel's endpoints (status, ask, thumbs up/down), the knowledge gaps report for training officers
	/// and protocol owners (turning a gap into a draft protocol change or a new training), and the knowledge settings (setting 139) with the
	/// index rebuild. Everything is behind the Ai.Knowledge entitlement; the free tier keeps Unified Search and gets nothing here.
	/// </summary>
	[Area("User")]
	[Authorize]
	[DepartmentLocalTime]
	public class KnowledgeController : SecureBaseController
	{
		/// <summary>TempData key carrying a gap's question to Trainings/New (kept out of the URL).</summary>
		public const string TrainingPrefillKey = "KnowledgeGapQuestion";
		public const int GapsPageSize = 50;

		private readonly IAiKnowledgeService _knowledge;
		private readonly IKnowledgeGapsService _gaps;
		private readonly IKnowledgeIndexService _index;
		private readonly IProtocolAccessService _protocolAccess;
		private readonly IProtocolLibraryService _library;
		private readonly IAuthorizationService _authorization;
		private readonly IDepartmentsService _departments;
		private readonly IUserProfileService _profiles;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Knowledge.Knowledge> _localizer;

		private Department _department;

		private static readonly System.Text.Json.JsonSerializerOptions CamelCase = new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase };

		public KnowledgeController(IAiKnowledgeService knowledge, IKnowledgeGapsService gaps, IKnowledgeIndexService index, IProtocolAccessService protocolAccess,
			IProtocolLibraryService library, IAuthorizationService authorization, IDepartmentsService departments, IUserProfileService profiles,
			IStringLocalizer<Resgrid.Localization.Areas.User.Knowledge.Knowledge> localizer)
		{
			_knowledge = knowledge;
			_gaps = gaps;
			_index = index;
			_protocolAccess = protocolAccess;
			_library = library;
			_authorization = authorization;
			_departments = departments;
			_profiles = profiles;
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

		private async Task<Department> DepartmentAsync() => _department ??= await _departments.GetDepartmentByIdAsync(DepartmentId, false);

		#region Ask

		[HttpGet]
		public async Task<IActionResult> Status(CancellationToken cancellationToken)
		{
			var status = await _knowledge.GetStatusAsync(Reader(), cancellationToken) ?? new KnowledgeStatus();
			return Json(new
			{
				available = status.Available,
				canSeeGaps = status.Available && status.CanSeeGaps,
				keptLocal = status.Available && status.QuestionsKeptLocal,
				gapsUrl = status.Available && status.CanSeeGaps ? Url.Action("Gaps", "Knowledge", new { area = "User" }) : null
			});
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Ask([FromForm] KnowledgeAskInput input, CancellationToken cancellationToken)
		{
			input ??= new KnowledgeAskInput();
			var answer = await _knowledge.AskAsync(new KnowledgeAskRequest
			{
				Reader = Reader(),
				Question = input.Question,
				Surface = Enum.IsDefined(typeof(KnowledgeAskSurfaces), input.Surface) ? input.Surface : (int)KnowledgeAskSurfaces.Library,
				// An item scope (a protocol or training page), or the whole Academy corpus (A3 "How do I…?" from the Help search).
				ScopeKind = input.ScopeKind.HasValue && Enum.IsDefined(typeof(KnowledgeSourceKinds), input.ScopeKind.Value) &&
					(!string.IsNullOrWhiteSpace(input.ScopeItemId) || input.ScopeKind.Value == (int)KnowledgeSourceKinds.Academy) ? input.ScopeKind : null,
				ScopeItemId = input.ScopeKind.HasValue && !string.IsNullOrWhiteSpace(input.ScopeItemId) ? input.ScopeItemId.Trim() : null,
				// K3 "Ask this course": the version the learner takes (the course's own source checks it is theirs).
				ScopeVersionId = input.ScopeKind.HasValue && !string.IsNullOrWhiteSpace(input.ScopeItemId) && !string.IsNullOrWhiteSpace(input.ScopeVersionId)
					? input.ScopeVersionId.Trim() : null,
				CallId = input.CallId,
				CallProtocolId = input.CallProtocolId
			}, cancellationToken);

			if (answer?.Outcome == KnowledgeOutcomes.NotEntitled)
				return NotFound();

			// The panel script reads camelCase; this app's JSON default keeps property names as declared.
			return Json(await ToViewAsync(answer), CamelCase);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Feedback([FromForm] string exchangeId, [FromForm] bool helpful, [FromForm] int surface, CancellationToken cancellationToken)
		{
			var recorded = await _knowledge.FeedbackAsync(Reader(), exchangeId, helpful, surface, cancellationToken);
			return Json(new { recorded, message = recorded ? (helpful ? L("FeedbackThanks") : L("FeedbackGap")) : L("ErrorNotice") });
		}

		private async Task<KnowledgeAskResultView> ToViewAsync(KnowledgeAnswer answer)
		{
			answer ??= new KnowledgeAnswer { Outcome = KnowledgeOutcomes.Unavailable };
			var department = await DepartmentAsync();
			var view = new KnowledgeAskResultView { Outcome = answer.Outcome, ExchangeId = answer.ExchangeId };

			IEnumerable<KnowledgeCitation> sources = answer.Citations;
			switch (answer.Outcome)
			{
				case KnowledgeOutcomes.Answered:
					view.Parts = answer.Parts.Select(p => new KnowledgePartView { Text = p.Text, Refs = p.Refs }).ToList();
					view.Conflicts = answer.Conflicts.Select(c => new KnowledgeConflictView { Note = c.Note, Refs = c.Refs }).ToList();
					break;
				case KnowledgeOutcomes.NotCovered:
					view.Message = L("NotCovered");
					sources = answer.Nearest;
					break;
				case KnowledgeOutcomes.Matched:
					view.Message = answer.QuestionKeptLocal ? L("KeptLocalNotice") : L("MatchedNotice");
					break;
				case KnowledgeOutcomes.BudgetExhausted:
					view.Message = L("BudgetNotice");
					break;
				case KnowledgeOutcomes.Busy:
					view.Message = L("BusyNotice");
					break;
				case KnowledgeOutcomes.Invalid:
					view.Message = L("QuestionRequired");
					break;
				default:
					view.Message = L("UnavailableNotice");
					break;
			}

			view.Citations = (sources ?? Enumerable.Empty<KnowledgeCitation>()).Select(c => Citation(c, department)).ToList();
			if (answer.EmbeddingsUnavailable && !answer.QuestionKeptLocal && view.Citations.Count > 0)
				view.Notices.Add(L("KeywordOnlyNotice"));
			if (answer.QuotesDose)
				view.Notices.Add(L("DoseNotice"));
			if (answer.OfflineCopyStale)
				view.Notices.Add(L("StaleCopyNotice"));
			if (answer.CallContextUsed && (answer.Outcome == KnowledgeOutcomes.Answered || answer.Outcome == KnowledgeOutcomes.NotCovered))
				view.Notices.Add(L("CallContextNotice"));
			view.DosingToolUrl = answer.DosingToolUrl;
			return view;
		}

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
			Kind = KindName(citation.SourceKind)
		};

		private string KindName(int sourceKind) => Enum.IsDefined(typeof(KnowledgeSourceKinds), sourceKind) ? L("Kind" + (KnowledgeSourceKinds)sourceKind) : string.Empty;

		#endregion Ask

		#region Gaps

		private async Task<bool> GapsAllowedAsync() =>
			await _knowledge.IsEntitledAsync(DepartmentId) && await _gaps.CanSeeGapsAsync(DepartmentId, UserId);

		[HttpGet]
		public async Task<IActionResult> Gaps(string status = "open", int page = 1, CancellationToken cancellationToken = default)
		{
			if (!await GapsAllowedAsync())
				return NotFound();

			var filter = (status ?? "open").ToLowerInvariant() switch
			{
				"resolved" => (int?)KnowledgeGapStatuses.Resolved,
				"dismissed" => (int?)KnowledgeGapStatuses.Dismissed,
				"all" => null,
				_ => (int?)KnowledgeGapStatuses.Open
			};
			page = Math.Max(1, page);
			var department = await DepartmentAsync();
			var protocolReader = await _protocolAccess.GetReaderAsync(DepartmentId, UserId);
			var trainingScope = await _authorization.GetTrainingManagementScopeAsync(UserId, DepartmentId);

			var model = new KnowledgeGapsModel
			{
				Status = filter.HasValue ? ((KnowledgeGapStatuses)filter.Value).ToString().ToLowerInvariant() : "all",
				Page = page,
				OpenCount = await _gaps.CountAsync(DepartmentId, (int)KnowledgeGapStatuses.Open, cancellationToken),
				ResolvedCount = await _gaps.CountAsync(DepartmentId, (int)KnowledgeGapStatuses.Resolved, cancellationToken),
				DismissedCount = await _gaps.CountAsync(DepartmentId, (int)KnowledgeGapStatuses.Dismissed, cancellationToken),
				CanAuthorProtocols = protocolReader?.MayAuthor == true,
				CanCreateTrainings = trainingScope?.CanCreate == true,
				IsAdmin = protocolReader?.IsDepartmentAdmin == true,
				Message = TempData["KnowledgeMessage"] as string,
				Error = TempData["KnowledgeError"] as string
			};

			var gaps = await _gaps.GetGapsAsync(DepartmentId, filter, (page - 1) * GapsPageSize, GapsPageSize + 1, cancellationToken);
			model.HasMore = gaps.Count > GapsPageSize;
			var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (var gap in gaps.Take(GapsPageSize))
			{
				var row = new KnowledgeGapRow
				{
					Gap = gap,
					Nearest = gap.NearestSources.Select(s => Citation(new KnowledgeCitation
					{
						SourceKind = s.SourceKind,
						ItemId = s.ItemId,
						VersionId = s.VersionId,
						VersionLabel = s.VersionLabel,
						Anchor = s.Anchor,
						Title = s.Title
					}, department)).ToList(),
					DraftProtocolId = ProtocolOf(gap)
				};
				if (!string.IsNullOrWhiteSpace(gap.UserId))
				{
					if (!names.TryGetValue(gap.UserId, out var name))
					{
						var profile = await _profiles.GetProfileByUserIdAsync(gap.UserId);
						name = profile?.FullName?.AsFirstNameLastName ?? string.Empty;
						names[gap.UserId] = name;
					}
					row.AskedBy = name;
				}
				model.Rows.Add(row);
			}

			return View(model);
		}

		/// <summary>The protocol a gap points at: the one it was asked from, else its closest protocol source.</summary>
		public static int? ProtocolOf(KnowledgeGap gap)
		{
			if (gap == null)
				return null;
			if ((gap.ContextKind == (int)KnowledgeSourceKinds.Protocol || gap.ContextKind == (int)KnowledgeSourceKinds.ProtocolFile) &&
				int.TryParse(gap.ContextItemId, NumberStyles.None, CultureInfo.InvariantCulture, out var context))
				return context;
			foreach (var source in gap.NearestSources)
				if ((source.SourceKind == (int)KnowledgeSourceKinds.Protocol || source.SourceKind == (int)KnowledgeSourceKinds.ProtocolFile) &&
					int.TryParse(source.ItemId, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
					return id;
			return null;
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> SetGapStatus(int id, int status, string note, string returnStatus, CancellationToken cancellationToken)
		{
			if (!await GapsAllowedAsync())
				return NotFound();
			if (!Enum.IsDefined(typeof(KnowledgeGapStatuses), status) || !await _gaps.SetStatusAsync(DepartmentId, id, (KnowledgeGapStatuses)status, UserId, note, cancellationToken))
				TempData["KnowledgeError"] = L("ErrorNotice");
			else
				TempData["KnowledgeMessage"] = L("GapUpdated");
			return RedirectToAction("Gaps", new { area = "User", status = returnStatus });
		}

		/// <summary>One click from a gap to a draft protocol change: the open draft (or a new one from the version in force), with the question as a suggestion. The author writes the change.</summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> DraftProtocol(int id, CancellationToken cancellationToken)
		{
			if (!await GapsAllowedAsync())
				return NotFound();
			var gap = await _gaps.GetGapAsync(DepartmentId, id, cancellationToken);
			var protocolId = ProtocolOf(gap);
			var reader = await _protocolAccess.GetReaderAsync(DepartmentId, UserId);
			if (gap == null || !protocolId.HasValue || reader == null || !reader.MayAuthor)
			{
				TempData["KnowledgeError"] = L("DraftFailed");
				return RedirectToAction("Gaps", new { area = "User" });
			}

			var draft = await _library.GetOpenDraftAsync(DepartmentId, protocolId.Value);
			if (draft == null)
			{
				var started = await _library.StartDraftAsync(reader, protocolId.Value, cancellationToken);
				if (started == null || !started.Succeeded)
				{
					TempData["KnowledgeError"] = L("DraftFailed");
					return RedirectToAction("Gaps", new { area = "User" });
				}
				draft = started.Version;
			}

			if (!string.IsNullOrWhiteSpace(gap.Question))
				await _library.AddCommentAsync(reader, protocolId.Value, draft?.ProtocolVersionId, ProtocolCommentKinds.Suggestion, null,
					string.Format(L("GapSuggestionBody"), gap.Question), null, cancellationToken);

			return RedirectToAction("Edit", "Protocols", new { area = "User", id = protocolId.Value });
		}

		/// <summary>One click from a gap to a new training pre-filled with the question (carried in TempData, not the URL); the author writes it.</summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> NewTraining(int id, CancellationToken cancellationToken)
		{
			if (!await GapsAllowedAsync())
				return NotFound();
			var gap = await _gaps.GetGapAsync(DepartmentId, id, cancellationToken);
			var scope = await _authorization.GetTrainingManagementScopeAsync(UserId, DepartmentId);
			if (gap == null || scope == null || !scope.CanCreate)
				return Unauthorized();

			TempData[TrainingPrefillKey] = string.IsNullOrWhiteSpace(gap.Question) ? L("GapTrainingNoQuestion") : gap.Question;
			return RedirectToAction("New", "Trainings", new { area = "User" });
		}

		#endregion Gaps

		#region Settings

		[HttpGet]
		public async Task<IActionResult> Settings(CancellationToken cancellationToken)
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin() || !await _knowledge.IsEntitledAsync(DepartmentId))
				return NotFound();

			var settings = await _knowledge.GetSettingsAsync(DepartmentId);
			return View(new KnowledgeSettingsModel
			{
				IncludeDocuments = settings.IncludeDocuments,
				DosingToolUrl = settings.DosingToolUrl,
				State = await _index.GetStateAsync(DepartmentId, cancellationToken),
				CanSeeGaps = true,
				Message = TempData["KnowledgeMessage"] as string
			});
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Settings(KnowledgeSettingsModel model, CancellationToken cancellationToken)
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin() || !await _knowledge.IsEntitledAsync(DepartmentId))
				return NotFound();

			model ??= new KnowledgeSettingsModel();
			var url = (model.DosingToolUrl ?? string.Empty).Trim();
			var settings = new KnowledgeSettings { IncludeDocuments = model.IncludeDocuments, DosingToolUrl = url };
			if (url.Length > 0 && new KnowledgeSettings { DosingToolUrl = url }.Normalize().DosingToolUrl == null)
			{
				model.Error = L("InvalidUrl");
				model.State = await _index.GetStateAsync(DepartmentId, cancellationToken);
				model.CanSeeGaps = true;
				return View(model);
			}

			await _knowledge.SaveSettingsAsync(DepartmentId, settings, UserId, cancellationToken);
			TempData["KnowledgeMessage"] = L("SettingsSaved");
			return RedirectToAction("Settings", new { area = "User" });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		public async Task<IActionResult> Rebuild(CancellationToken cancellationToken)
		{
			if (!ClaimsAuthorizationHelper.IsUserDepartmentAdmin() || !await _knowledge.IsEntitledAsync(DepartmentId))
				return NotFound();

			await _index.RequestRebuildAsync(DepartmentId, UserId, cancellationToken);
			TempData["KnowledgeMessage"] = L("RebuildQueued");
			return RedirectToAction("Settings", new { area = "User" });
		}

		#endregion Settings
	}
}
