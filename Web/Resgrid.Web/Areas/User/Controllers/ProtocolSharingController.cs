using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Helpers;
using Resgrid.WebCore.Areas.User.Models.Protocols;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// Protocols P5 on the web: opt-in public pages (approvers), the Resgrid template library (authors clone), the community exchange (every
	/// member browses; approvers submit protocols and training creators trainings; authors import; Resgrid system operators review), the open
	/// format and HTML/PDF export of a set, and the protocol analytics dashboard (managers, free). Every POST carries antiforgery.
	/// </summary>
	[Area("User")]
	[Resgrid.Web.Helpers.DepartmentLocalTime]
	public class ProtocolSharingController : SecureBaseController
	{
		private const string MessageKey = "SharingMessage";
		private const string ErrorKey = "SharingError";

		private readonly IProtocolAccessService _access;
		private readonly IProtocolsService _protocolsService;
		private readonly IProtocolLibraryService _library;
		private readonly IProtocolPublicPageService _publicPages;
		private readonly IProtocolTemplateLibraryService _templates;
		private readonly ICommunityExchangeService _exchange;
		private readonly IProtocolAnalyticsService _analytics;
		private readonly IProtocolBookService _books;
		private readonly IDepartmentsService _departments;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.ProtocolSharing.ProtocolSharing> _text;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Protocols.Protocols> _protocolText;
		private readonly IDepartmentSettingsService _departmentSettings;

		public ProtocolSharingController(IProtocolAccessService access, IProtocolsService protocolsService, IProtocolLibraryService library,
			IProtocolPublicPageService publicPages, IProtocolTemplateLibraryService templates, ICommunityExchangeService exchange, IProtocolAnalyticsService analytics,
			IProtocolBookService books, IDepartmentsService departments, IStringLocalizer<Resgrid.Localization.Areas.User.ProtocolSharing.ProtocolSharing> text,
			IStringLocalizer<Resgrid.Localization.Areas.User.Protocols.Protocols> protocolText, IDepartmentSettingsService departmentSettings)
		{
			_departmentSettings = departmentSettings;
			_access = access;
			_protocolsService = protocolsService;
			_library = library;
			_publicPages = publicPages;
			_templates = templates;
			_exchange = exchange;
			_analytics = analytics;
			_books = books;
			_departments = departments;
			_text = text;
			_protocolText = protocolText;
		}

		private string Text(string key) => _text[key].Value;

		private bool IsReviewer => User?.IsInRole(SystemRoles.Admins) ?? false;

		private async Task<ExchangeActor> ActorAsync(ProtocolReader reader) => new ExchangeActor
		{
			DepartmentId = DepartmentId,
			UserId = UserId,
			DepartmentName = (await _departments.GetDepartmentByIdAsync(DepartmentId, false))?.Name,
			IsCurrentMember = reader.IsCurrentMember,
			IsDepartmentAdmin = reader.IsDepartmentAdmin,
			CanAuthorProtocols = reader.CanAuthor,
			CanApproveProtocols = reader.CanApprove,
			CanCreateTrainings = User?.HasClaim(ResgridClaimTypes.Resources.Training, ResgridClaimTypes.Actions.Create) ?? false,
			IsReviewer = IsReviewer
		};

		private T Fill<T>(T model, ProtocolReader reader, ExchangeActor actor = null) where T : ProtocolSharingPageModel
		{
			model.CanApprove = reader.MayApprove;
			model.CanAuthor = reader.MayAuthor;
			model.IsManager = reader.IsManager;
			model.IsReviewer = IsReviewer;
			model.CanSubmit = actor != null && _exchange.CanSubmitAny(actor);
			model.Message ??= TempData?[MessageKey] as string;
			model.Error ??= TempData?[ErrorKey] as string;
			return model;
		}

		private void Say(string message) { if (TempData != null) TempData[MessageKey] = message; }

		private void Fail(string message) { if (TempData != null) TempData[ErrorKey] = message; }

		private string ExchangeError(ExchangeErrors error) => Text("ExchangeError" + error);

		#region Public pages

		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> Public()
		{
			var reader = await _access.GetReaderAsync(DepartmentId, UserId);
			if (!reader.MayApprove)
				return Unauthorized();

			var model = Fill(new ProtocolPublicLinksModel
			{
				Links = await _publicPages.GetLinksAsync(reader, true),
				Categories = await _library.GetCategoriesAsync(DepartmentId) ?? new List<ProtocolCategory>(),
				BaseUrl = (Config.SystemBehaviorConfig.ResgridBaseUrl ?? string.Empty).TrimEnd('/')
			}, reader, await ActorAsync(reader));

			var headers = (await _library.GetDepartmentVersionHeadersAsync(DepartmentId) ?? new List<ProtocolVersion>()).ToDictionary(v => v.ProtocolVersionId);
			var audiences = (await _library.GetDepartmentAudiencesAsync(DepartmentId) ?? new List<ProtocolAudience>()).ToLookup(a => a.DispatchProtocolId);
			foreach (var protocol in (await _protocolsService.GetProtocolSummariesForDepartmentAsync(DepartmentId) ?? new List<DispatchProtocol>())
				.Where(p => !p.IsDeleted && !p.IsBulletin).OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
			{
				var current = protocol.CurrentVersionId.HasValue && headers.TryGetValue(protocol.CurrentVersionId.Value, out var v) ? v : null;
				var eligibility = ProtocolPublicPages.Eligibility(protocol, current, audiences[protocol.DispatchProtocolId]);
				if (eligibility == ProtocolPublicEligibility.Eligible)
					model.Eligible.Add(protocol);
				else
					model.NotEligible.Add(new KeyValuePair<DispatchProtocol, ProtocolPublicEligibility>(protocol, eligibility));
			}

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> CreatePublicLink(int protocolId, bool pinVersion, bool embedEnabled, string embedOrigins, CancellationToken cancellationToken = default)
		{
			var reader = await _access.GetReaderAsync(DepartmentId, UserId);
			var result = await _publicPages.CreateProtocolLinkAsync(reader, protocolId, pinVersion, embedEnabled, embedOrigins, cancellationToken);
			Report(result);
			return RedirectToAction("Public");
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> CreatePublicSetLink(int categoryId, bool embedEnabled, string embedOrigins, CancellationToken cancellationToken = default)
		{
			var reader = await _access.GetReaderAsync(DepartmentId, UserId);
			var result = await _publicPages.CreateCategoryLinkAsync(reader, categoryId, embedEnabled, embedOrigins, cancellationToken);
			Report(result);
			return RedirectToAction("Public");
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> RevokePublicLink(int id, CancellationToken cancellationToken = default)
		{
			var reader = await _access.GetReaderAsync(DepartmentId, UserId);
			var result = await _publicPages.RevokeAsync(reader, id, cancellationToken);
			if (result.Succeeded)
				Say(Text("PublicRevoked"));
			else
				Fail(Text("PublicError" + result.Error));
			return RedirectToAction("Public");
		}

		private void Report(ProtocolPublicLinkResult result)
		{
			if (result.Succeeded)
				Say(Text("PublicCreated"));
			else if (result.Error == ProtocolPublicLinkErrors.NotEligible)
				Fail(Text("Eligibility" + result.Eligibility));
			else
				Fail(Text("PublicError" + result.Error));
		}

		#endregion Public pages

		#region Template library

		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> Templates()
		{
			var reader = await _access.GetReaderAsync(DepartmentId, UserId);
			// Round 9C: sets in the reader's language; the ones the operating profile suggests come first (nothing is hidden).
			var profile = await _departmentSettings.GetOperatingProfileAsync(DepartmentId);
			var sets = _templates.GetSets(System.Globalization.CultureInfo.CurrentUICulture.Name);
			return View(Fill(new ProtocolTemplatesModel
			{
				Sets = SeedSectorRecommendations.SuggestedFirst(sets, SeedContentLocalization.ProtocolTemplates, s => s.Set?.Key, profile),
				Suggested = SeedSectorRecommendations.Suggested(SeedContentLocalization.ProtocolTemplates, sets.Select(s => s.Set?.Key), profile)
			}, reader, await ActorAsync(reader)));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_Create)]
		public async Task<IActionResult> CloneTemplate(string key, List<string> codes, CancellationToken cancellationToken = default)
		{
			var reader = await _access.GetReaderAsync(DepartmentId, UserId);
			var result = await _templates.CloneAsync(reader, key, codes, System.Globalization.CultureInfo.CurrentUICulture.Name, cancellationToken);
			if (result.Error != null)
			{
				Fail(Text("TemplateError_" + result.Error));
				return RedirectToAction("Templates");
			}

			Say(string.Format(Text("TemplateCloned"), result.Created) + (result.Skipped.Count > 0 ? " " + string.Format(Text("TemplateSkipped"), result.Skipped.Count) : string.Empty));
			return RedirectToAction("Index", "Protocols", new { area = "User", status = "draft" });
		}

		#endregion Template library

		#region Community exchange

		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> Exchange(string q = null, int? kind = null, string sort = null, int page = 1)
		{
			var reader = await _access.GetReaderAsync(DepartmentId, UserId);
			var query = new ExchangeBrowseQuery
			{
				Search = string.IsNullOrWhiteSpace(q) ? null : q.Trim(),
				ItemKind = kind.HasValue && Enum.IsDefined(typeof(ExchangeItemKinds), kind.Value) ? kind : null,
				Sort = sort is "rating" or "imports" ? sort : "newest",
				Take = 20
			};
			page = Math.Max(1, page);
			query.Skip = (page - 1) * query.Take;
			var (items, total) = await _exchange.BrowseAsync(query);
			return View(Fill(new ExchangeBrowseModel
			{
				Query = query,
				Items = items,
				Total = total,
				Kinds = _exchange.GetKinds(),
				Page = page,
				Pages = Math.Max(1, (int)Math.Ceiling(total / (double)query.Take))
			}, reader, await ActorAsync(reader)));
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> Listing(int id)
		{
			var reader = await _access.GetReaderAsync(DepartmentId, UserId);
			var actor = await ActorAsync(reader);
			var view = await _exchange.GetListingAsync(actor, id);
			if (view == null)
				return NotFound();
			return View(Fill(new ExchangeListingModel { View = view }, reader, actor));
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> Submit(int kind = 0, string item = null)
		{
			var reader = await _access.GetReaderAsync(DepartmentId, UserId);
			var actor = await ActorAsync(reader);
			if (!_exchange.CanSubmitAny(actor))
				return Unauthorized();

			var kinds = _exchange.GetKinds();
			if (!kinds.Contains((ExchangeItemKinds)kind))
				kind = (int)(kinds.FirstOrDefault());
			var model = Fill(new ExchangeSubmitModel
			{
				Kinds = kinds,
				Kind = kind,
				Items = await _exchange.GetSubmittableAsync(actor, (ExchangeItemKinds)kind),
				SourceRef = item,
				DepartmentName = actor.DepartmentName,
				AttributionName = actor.DepartmentName
			}, reader, actor);

			if (!string.IsNullOrWhiteSpace(item))
			{
				var preview = await _exchange.PreviewSubmissionAsync(actor, (ExchangeItemKinds)kind, item);
				if (preview.Succeeded)
				{
					model.Prepared = preview.Prepared;
					model.Summary = preview.Prepared.Summary;
					model.PreviewHtml = preview.Prepared.PreviewHtml;
				}
				else
				{
					model.Error = ExchangeError(preview.Error);
				}
			}

			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> Submit(int kind, string item, string summary, string license, string attributionName, bool attest, CancellationToken cancellationToken = default)
		{
			var reader = await _access.GetReaderAsync(DepartmentId, UserId);
			var actor = await ActorAsync(reader);
			var result = await _exchange.SubmitAsync(actor, new ExchangeSubmitRequest
			{
				ItemKind = kind,
				SourceRef = item,
				Summary = summary,
				License = license,
				AttributionName = attributionName,
				Attested = attest
			}, cancellationToken);

			if (!result.Succeeded)
			{
				Fail(ExchangeError(result.Error));
				return RedirectToAction("Submit", new { kind, item });
			}

			Say(Text("ExchangeSubmitted"));
			return RedirectToAction("Listing", new { id = result.Listing.ExchangeListingId });
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> MySubmissions()
		{
			var reader = await _access.GetReaderAsync(DepartmentId, UserId);
			var actor = await ActorAsync(reader);
			return View("ExchangeList", Fill(new ExchangeListModel { Mode = "mine", Items = await _exchange.GetMySubmissionsAsync(actor) }, reader, actor));
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.SystemAdmin)]
		public async Task<IActionResult> ExchangeReview()
		{
			var reader = await _access.GetReaderAsync(DepartmentId, UserId);
			var actor = await ActorAsync(reader);
			return View("ExchangeList", Fill(new ExchangeListModel { Mode = "review", Items = await _exchange.GetReviewQueueAsync(actor) }, reader, actor));
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.SystemAdmin)]
		public async Task<IActionResult> Decide(int id, bool approve, string note, CancellationToken cancellationToken = default)
		{
			var reader = await _access.GetReaderAsync(DepartmentId, UserId);
			var result = await _exchange.DecideAsync(await ActorAsync(reader), id, approve, note, cancellationToken);
			if (result.Succeeded) Say(Text(approve ? "ExchangeListed" : "ExchangeRejected")); else Fail(ExchangeError(result.Error));
			return RedirectToAction("ExchangeReview");
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.SystemAdmin)]
		public async Task<IActionResult> RemoveListing(int id, string note, CancellationToken cancellationToken = default)
		{
			var reader = await _access.GetReaderAsync(DepartmentId, UserId);
			var result = await _exchange.RemoveAsync(await ActorAsync(reader), id, note, cancellationToken);
			if (result.Succeeded) Say(Text("ExchangeRemoved")); else Fail(ExchangeError(result.Error));
			return RedirectToAction("Listing", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> Withdraw(int id, CancellationToken cancellationToken = default)
		{
			var reader = await _access.GetReaderAsync(DepartmentId, UserId);
			var result = await _exchange.WithdrawAsync(await ActorAsync(reader), id, cancellationToken);
			if (result.Succeeded) Say(Text("ExchangeWithdrawn")); else Fail(ExchangeError(result.Error));
			return RedirectToAction("MySubmissions");
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> Rate(int id, int stars, CancellationToken cancellationToken = default)
		{
			var reader = await _access.GetReaderAsync(DepartmentId, UserId);
			var result = await _exchange.RateAsync(await ActorAsync(reader), id, stars, cancellationToken);
			if (result.Succeeded) Say(Text("ExchangeRated")); else Fail(ExchangeError(result.Error));
			return RedirectToAction("Listing", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> Comment(int id, string body, CancellationToken cancellationToken = default)
		{
			var reader = await _access.GetReaderAsync(DepartmentId, UserId);
			var result = await _exchange.CommentAsync(await ActorAsync(reader), id, body, cancellationToken);
			if (result.Succeeded) Say(Text("ExchangeCommented")); else Fail(ExchangeError(result.Error));
			return RedirectToAction("Listing", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> HideComment(int id, int commentId, CancellationToken cancellationToken = default)
		{
			var reader = await _access.GetReaderAsync(DepartmentId, UserId);
			var result = await _exchange.HideCommentAsync(await ActorAsync(reader), commentId, cancellationToken);
			if (result.Succeeded) Say(Text("ExchangeCommentHidden")); else Fail(ExchangeError(result.Error));
			return RedirectToAction("Listing", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> Import(int id, CancellationToken cancellationToken = default)
		{
			var reader = await _access.GetReaderAsync(DepartmentId, UserId);
			var result = await _exchange.ImportAsync(await ActorAsync(reader), id, cancellationToken);
			if (!result.Succeeded)
			{
				Fail(ExchangeError(result.Error));
				return RedirectToAction("Listing", new { id });
			}

			Say(Text("ExchangeImported"));
			return (ExchangeItemKinds)result.Listing.ItemKind switch
			{
				ExchangeItemKinds.Protocol => RedirectToAction("View", "Protocols", new { area = "User", id = result.CreatedItemId }),
				ExchangeItemKinds.Training => RedirectToAction("Index", "Trainings", new { area = "User" }),
				ExchangeItemKinds.SkillSheet => RedirectToAction("Sheet", "LmsSkills", new { area = "User", id = result.CreatedItemId }),
				ExchangeItemKinds.LessonPlan => RedirectToAction("LessonPlan", "LmsClasses", new { area = "User", id = result.CreatedItemId }),
				_ => RedirectToAction("Listing", new { id })
			};
		}

		#endregion Community exchange

		#region Open format and export

		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_Update)]
		public async Task<IActionResult> Export()
		{
			var reader = await _access.GetReaderAsync(DepartmentId, UserId);
			if (!reader.IsManager)
				return Unauthorized();
			var readable = await _access.FilterReadableAsync(reader, await _protocolsService.GetProtocolSummariesForDepartmentAsync(DepartmentId) ?? new List<DispatchProtocol>());
			return View(Fill(new ProtocolOpenFormatModel
			{
				Categories = await _library.GetCategoriesAsync(DepartmentId) ?? new List<ProtocolCategory>(),
				ReadableCount = readable.Count(p => !p.IsDeleted)
			}, reader, await ActorAsync(reader)));
		}

		/// <summary>The open format (version 2) of the library or one category (with its sub-categories) as a set.</summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_Update)]
		public async Task<IActionResult> ExportJson(int? categoryId = null)
		{
			var reader = await _access.GetReaderAsync(DepartmentId, UserId);
			if (!reader.IsManager)
				return Unauthorized();

			var categories = await _library.GetCategoriesAsync(DepartmentId) ?? new List<ProtocolCategory>();
			ProtocolCategory category = null;
			var ids = new List<int>();
			if (categoryId.HasValue)
			{
				category = categories.FirstOrDefault(c => c.ProtocolCategoryId == categoryId.Value);
				if (category == null)
					return NotFound();
				var within = Descendants(categories, category.ProtocolCategoryId);
				ids = (await _protocolsService.GetProtocolSummariesForDepartmentAsync(DepartmentId) ?? new List<DispatchProtocol>())
					.Where(p => p.CategoryId.HasValue && within.Contains(p.CategoryId.Value)).Select(p => p.DispatchProtocolId).ToList();
				if (ids.Count == 0)
					ids.Add(-1);
			}

			var document = await _library.ExportAsync(reader, ids);
			document.Protocols = document.Protocols.Where(p => p != null).ToList();
			var department = await _departments.GetDepartmentByIdAsync(DepartmentId, false);
			document.Set = new ProtocolExchangeSet
			{
				Key = category == null ? "library" : "category-" + category.ProtocolCategoryId.ToString(CultureInfo.InvariantCulture),
				Title = category?.Name ?? Text("ExportLibraryTitle"),
				Publisher = department?.Name,
				SetVersion = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
			};

			Response.Headers["Cache-Control"] = "no-store";
			return File(Encoding.UTF8.GetBytes(document.ToJson()), "application/json",
				"protocols-" + document.Set.Key + "-" + DateTime.UtcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".json");
		}

		/// <summary>A set as one self-contained HTML file (the PDF book's rendering): what the member may read, versions in force.</summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> ExportHtml(int? categoryId = null)
		{
			var book = await _books.BuildAsync(DepartmentId, UserId, categoryId, new ProtocolBookText
			{
				LibraryTitle = _protocolText["BookLibraryTitle"].Value,
				Contents = _protocolText["BookContents"].Value,
				Version = _protocolText["BookVersion"].Value,
				EffectiveOn = _protocolText["BookEffective"].Value,
				ApprovedBy = _protocolText["BookApprovedBy"].Value,
				ContentHash = _protocolText["BookContent"].Value,
				GeneratedOn = _protocolText["BookGenerated"].Value,
				DocumentBacked = _protocolText["BookDocumentBacked"].Value,
				Empty = _protocolText["BookEmpty"].Value,
				Uncategorized = _protocolText["BookUncategorized"].Value,
				DosingToolLink = _protocolText["DosingToolLinkText"].Value
			});
			if (book == null)
				return NotFound();

			// A download, never rendered on this origin; the sandbox keeps it inert if a browser shows it anyway.
			Response.Headers["Cache-Control"] = "no-store";
			Response.Headers["Content-Security-Policy"] = "sandbox; default-src 'none'; style-src 'unsafe-inline'; img-src https: data:";
			Response.Headers["X-Content-Type-Options"] = "nosniff";
			var subject = categoryId.HasValue ? "category-" + categoryId.Value.ToString(CultureInfo.InvariantCulture) : "library";
			return File(Encoding.UTF8.GetBytes(book.Html), "text/html", "protocols-" + subject + "-" + book.GeneratedOn.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".html");
		}

		private static HashSet<int> Descendants(List<ProtocolCategory> categories, int root)
		{
			var set = new HashSet<int> { root };
			var added = true;
			while (added)
			{
				added = false;
				foreach (var category in categories)
					if (category.ParentCategoryId.HasValue && set.Contains(category.ParentCategoryId.Value) && set.Add(category.ProtocolCategoryId))
						added = true;
			}
			return set;
		}

		#endregion Open format and export

		#region Analytics

		private static (DateTime From, DateTime To) Period(string from, string to)
		{
			var now = DateTime.UtcNow.Date;
			var end = DateTime.TryParseExact(to, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t) ? t.Date.AddDays(1) : now.AddDays(1);
			var start = DateTime.TryParseExact(from, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var f) ? f.Date : end.AddDays(-30);
			return (start, end);
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> Analytics(string from = null, string to = null)
		{
			var reader = await _access.GetReaderAsync(DepartmentId, UserId);
			if (!reader.IsManager)
				return Unauthorized();

			var (start, end) = Period(from, to);
			var report = await _analytics.GetReportAsync(reader, start, end);
			return View(Fill(new ProtocolAnalyticsModel
			{
				Report = report,
				From = (report?.FromUtc ?? start).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
				To = (report?.ToUtc ?? end).AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
			}, reader, await ActorAsync(reader)));
		}

		public static readonly string[] CsvHeaderKeys =
		{
			"CsvNumber", "CsvName", "CsvCalls", "CsvByTrigger", "CsvByDispatcher", "CsvByAi", "CsvRemoved", "CsvVersions", "CsvWithDeterminant", "CsvOverridden",
			"CsvOverrideRate", "CsvDeterminants", "CsvTimedCalls", "CsvMedianMinutes", "CsvMeanMinutes", "CsvAckAssigned", "CsvAckSigned", "CsvAckOverdue", "CsvAckCompliance"
		};

		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> AnalyticsCsv(string from = null, string to = null)
		{
			var reader = await _access.GetReaderAsync(DepartmentId, UserId);
			if (!reader.IsManager)
				return Unauthorized();

			var (start, end) = Period(from, to);
			var csv = await _analytics.ExportCsvAsync(reader, start, end, CsvHeaderKeys.Select(Text).ToList());
			if (csv == null)
				return Unauthorized();

			Response.Headers["Cache-Control"] = "no-store";
			return File(new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray(), "text/csv",
				"protocol-analytics-" + start.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "-" + end.AddDays(-1).ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".csv");
		}

		#endregion Analytics
	}
}
