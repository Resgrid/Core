using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Security;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Areas.User.Models.Signatures;
using Resgrid.Web.Helpers;
using Resgrid.WebCore.Areas.User.Models.Protocols;
using IAuthorizationService = Resgrid.Model.Services.IAuthorizationService;
using IdentityUser = Resgrid.Model.Identity.IdentityUser;

namespace Resgrid.Web.Areas.User.Controllers
{
	/// <summary>
	/// Protocols (P1): the library, the detail page, the creation wizard and the full-page editor, the review queue and review
	/// (diff-first, approve with an L2 signature), publish, compare, categories, settings, import and export, and the JSON the
	/// call forms' protocol panel reads. Every read applies the protocol read rule (R-P4). Attachment bytes leave only through
	/// <see cref="GetProtocolAttachment"/>.
	/// </summary>
	[Area("User")]
	[Resgrid.Web.Helpers.DepartmentLocalTime]
	public class ProtocolsController : SecureBaseController
	{
		private readonly IProtocolsService _protocolsService;
		private readonly ICallsService _callsService;
		private readonly IAuthorizationService _authorizationService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IEventAggregator _eventAggregator;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Protocols.Protocols> _localizer;
		private readonly IProtocolAccessService _access;
		private readonly IProtocolLibraryService _library;
		private readonly ISignatureService _signatures;
		private readonly IDepartmentGroupsService _groups;
		private readonly IPersonnelRolesService _roles;
		private readonly IShiftsService _shifts;
		private readonly IUnitsService _units;
		private readonly ICertificationService _certifications;
		private readonly IPasskeyService _passkeys;
		private readonly UserManager<IdentityUser> _userManager;
		private readonly IStringLocalizer<Resgrid.Localization.Areas.User.Signatures.Signatures> _signatureText;

		private readonly IMappingService _mappingService;

		public ProtocolsController(IProtocolsService protocolsService, ICallsService callsService, IAuthorizationService authorizationService, IDepartmentsService departmentsService,
			IMappingService mappingService, IEventAggregator eventAggregator = null, IStringLocalizer<Resgrid.Localization.Areas.User.Protocols.Protocols> localizer = null,
			IProtocolAccessService access = null, IProtocolLibraryService library = null, ISignatureService signatures = null, IDepartmentGroupsService groups = null,
			IPersonnelRolesService roles = null, IShiftsService shifts = null, IUnitsService units = null, ICertificationService certifications = null,
			IPasskeyService passkeys = null, UserManager<IdentityUser> userManager = null,
			IStringLocalizer<Resgrid.Localization.Areas.User.Signatures.Signatures> signatureText = null)
		{
			_protocolsService = protocolsService;
			_callsService = callsService;
			_authorizationService = authorizationService;
			_departmentsService = departmentsService;
			_mappingService = mappingService;
			_eventAggregator = eventAggregator;
			_localizer = localizer;
			_access = access;
			_library = library;
			_signatures = signatures;
			_groups = groups;
			_roles = roles;
			_shifts = shifts;
			_units = units;
			_certifications = certifications;
			_passkeys = passkeys;
			_userManager = userManager;
			_signatureText = signatureText;
		}

		private bool HasProtocolClaim(string action) => User?.HasClaim(ResgridClaimTypes.Resources.Protocols, action) ?? false;

		private string Text(string key) => _localizer?[key]?.Value ?? key;

		private string Signing(string key) => _signatureText?[key]?.Value ?? key;

		private async Task<ProtocolReader> ReaderAsync() =>
			_access == null ? ProtocolReader.Nobody(DepartmentId) : await _access.GetReaderAsync(DepartmentId, UserId);

		private string LifecycleError(ProtocolLifecycleErrors error) => Text("LifecycleError" + error);

		private async Task<Dictionary<string, string>> NamesAsync()
		{
			var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (var person in await _departmentsService.GetAllPersonnelNamesForDepartmentAsync(DepartmentId) ?? new List<PersonName>())
				if (person != null && !string.IsNullOrWhiteSpace(person.UserId))
					names[person.UserId] = person.Name?.Trim();
			return names;
		}

		private static string NameOf(Dictionary<string, string> names, string userId) =>
			string.IsNullOrWhiteSpace(userId) ? null : names.TryGetValue(userId, out var name) && !string.IsNullOrWhiteSpace(name) ? name : userId;

		/// <summary>The pill a protocol shows: what a member would be told about it.</summary>
		public static string StatusKey(DispatchProtocol protocol, ProtocolVersion open)
		{
			if (protocol.IsDeleted) return "deleted";
			if (protocol.Status == (int)ProtocolStatuses.Retired) return "retired";
			if (open != null && protocol.Status == (int)ProtocolStatuses.Draft)
			{
				if (open.Status == (int)ProtocolVersionStatuses.InReview) return "review";
				if (open.Status == (int)ProtocolVersionStatuses.Approved) return open.EffectiveOn.HasValue ? "scheduled" : "approved";
				return "draft";
			}
			if (protocol.Status == (int)ProtocolStatuses.Draft) return "draft";
			if (protocol.IsDisabled) return "disabled";
			return "published";
		}

		#region Library and detail

		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> Index(string q = null, int? kind = null, int? category = null, string status = null, string owner = null, string review = null,
			string audience = null)
		{
			var reader = await ReaderAsync();
			var all = await _protocolsService.GetProtocolSummariesForDepartmentAsync(DepartmentId);
			var readable = _access == null ? new List<DispatchProtocol>() : await _access.FilterReadableAsync(reader, all);
			var versions = _library == null ? new List<ProtocolVersion>() : await _library.GetDepartmentVersionHeadersAsync(DepartmentId);
			var byId = versions.ToDictionary(v => v.ProtocolVersionId);
			var openByProtocol = versions.Where(v => v.Status == (int)ProtocolVersionStatuses.Draft || v.Status == (int)ProtocolVersionStatuses.InReview || v.Status == (int)ProtocolVersionStatuses.Approved)
				.GroupBy(v => v.DispatchProtocolId).ToDictionary(g => g.Key, g => g.OrderByDescending(v => v.ProtocolVersionId).First());
			var audiences = _library == null ? new List<ProtocolAudience>() : await _library.GetDepartmentAudiencesAsync(DepartmentId);
			var restricted = audiences.Select(a => a.DispatchProtocolId).ToHashSet();
			var categories = _library == null ? new List<ProtocolCategory>() : await _library.GetCategoriesAsync(DepartmentId);
			var usage = await _protocolsService.GetUsageCountsAsync(DepartmentId, DateTime.UtcNow.AddDays(-30));
			var priorities = await _callsService.GetCallPrioritiesForDepartmentAsync(DepartmentId) ?? new List<DepartmentCallPriority>();
			var types = await _callsService.GetCallTypesForDepartmentAsync(DepartmentId) ?? new List<CallType>();
			var names = await NamesAsync();
			var (favorites, recent) = _library == null ? (new List<int>(), new List<int>()) : await _library.GetFavoritesAndRecentAsync(DepartmentId, UserId);
			var now = DateTime.UtcNow;

			ProtocolLibraryRow Row(DispatchProtocol p) => new ProtocolLibraryRow
			{
				Protocol = p,
				Current = p.CurrentVersionId.HasValue && byId.TryGetValue(p.CurrentVersionId.Value, out var current) ? current : null,
				// Members never see another person's working copy, only managers do.
				Open = reader.IsManager && openByProtocol.TryGetValue(p.DispatchProtocolId, out var open) ? open : null,
				CategoryName = p.CategoryId.HasValue ? categories.FirstOrDefault(c => c.ProtocolCategoryId == p.CategoryId.Value)?.Name : null,
				OwnerName = NameOf(names, p.OwnerUserId),
				CallsLast30Days = usage.TryGetValue(p.DispatchProtocolId, out var calls) ? calls : 0,
				IsFavorite = favorites.Contains(p.DispatchProtocolId),
				HasAudience = restricted.Contains(p.DispatchProtocolId),
				Triggers = (p.Triggers ?? Enumerable.Empty<DispatchProtocolTrigger>()).Select(t => ProtocolDisplay.Describe(t, priorities, types, _localizer)).ToList(),
				ReviewOverdue = p.IsPublished && p.NextReviewOn.HasValue && p.NextReviewOn.Value <= now,
				ReviewDueSoon = p.IsPublished && p.NextReviewOn.HasValue && p.NextReviewOn.Value > now && p.NextReviewOn.Value <= now.AddDays(30)
			};

			var rows = readable.Select(Row).ToList();
			foreach (var row in rows)
				row.StatusKey = StatusKey(row.Protocol, row.Open);

			var model = new ProtocolLibraryModel
			{
				Query = q?.Trim(),
				Kind = kind.HasValue && ProtocolKindRules.IsKnown(kind.Value) ? kind : null,
				CategoryId = category,
				Status = status is "published" or "draft" or "review" or "retired" or "disabled" ? status : "",
				Owner = owner,
				Review = review is "due" or "overdue" ? review : "",
				Audience = audience is "everyone" or "restricted" ? audience : "",
				Total = rows.Count,
				PublishedCount = rows.Count(r => r.Protocol.IsPublished && !r.Protocol.IsDisabled),
				InReviewCount = rows.Count(r => r.Open != null && r.Open.Status == (int)ProtocolVersionStatuses.InReview),
				DueForReviewCount = rows.Count(r => r.ReviewOverdue || r.ReviewDueSoon),
				UsedOnCallsCount = rows.Count(r => r.CallsLast30Days > 0),
				Categories = categories,
				CategoryCounts = rows.Where(r => r.Protocol.CategoryId.HasValue).GroupBy(r => r.Protocol.CategoryId.Value).ToDictionary(g => g.Key, g => g.Count()),
				Owners = rows.Where(r => !string.IsNullOrWhiteSpace(r.Protocol.OwnerUserId)).GroupBy(r => r.Protocol.OwnerUserId, StringComparer.OrdinalIgnoreCase)
					.ToDictionary(g => g.Key, g => g.First().OwnerName, StringComparer.OrdinalIgnoreCase),
				CanCreate = reader.MayAuthor,
				CanApprove = reader.MayApprove,
				IsManager = reader.IsManager,
				IsAdmin = reader.IsDepartmentAdmin,
				CanDelete = HasProtocolClaim(ResgridClaimTypes.Actions.Delete),
				Message = TempData?["ProtocolMessage"] as string
			};

			model.Favorites = favorites.Select(id => rows.FirstOrDefault(r => r.Protocol.DispatchProtocolId == id)).Where(r => r != null).ToList();
			model.Recent = recent.Select(id => rows.FirstOrDefault(r => r.Protocol.DispatchProtocolId == id)).Where(r => r != null).Take(5).ToList();

			IEnumerable<ProtocolLibraryRow> shown = rows;
			if (!string.IsNullOrWhiteSpace(model.Query))
				shown = shown.Where(r => (r.Protocol.Name ?? "").Contains(model.Query, StringComparison.CurrentCultureIgnoreCase) ||
					(r.Protocol.Code ?? "").Contains(model.Query, StringComparison.OrdinalIgnoreCase) ||
					(r.Protocol.Number ?? "").Contains(model.Query, StringComparison.OrdinalIgnoreCase) ||
					(r.Protocol.Tags ?? "").Contains(model.Query, StringComparison.CurrentCultureIgnoreCase));
			if (model.Kind.HasValue)
				shown = shown.Where(r => r.Protocol.Kind == model.Kind.Value);
			if (model.CategoryId.HasValue)
			{
				var within = Descendants(categories, model.CategoryId.Value);
				shown = shown.Where(r => r.Protocol.CategoryId.HasValue && within.Contains(r.Protocol.CategoryId.Value));
			}
			if (!string.IsNullOrEmpty(model.Status))
				shown = shown.Where(r => model.Status switch
				{
					"review" => r.StatusKey is "review" or "approved" or "scheduled" || (r.Open != null && r.Open.Status != (int)ProtocolVersionStatuses.Draft),
					"draft" => r.StatusKey == "draft" || (r.Open != null && r.Open.Status == (int)ProtocolVersionStatuses.Draft),
					_ => r.StatusKey == model.Status
				});
			if (!string.IsNullOrWhiteSpace(model.Owner))
				shown = shown.Where(r => string.Equals(r.Protocol.OwnerUserId, model.Owner, StringComparison.OrdinalIgnoreCase));
			if (model.Review == "due")
				shown = shown.Where(r => r.ReviewDueSoon || r.ReviewOverdue);
			else if (model.Review == "overdue")
				shown = shown.Where(r => r.ReviewOverdue);
			if (model.Audience == "everyone")
				shown = shown.Where(r => !r.HasAudience);
			else if (model.Audience == "restricted")
				shown = shown.Where(r => r.HasAudience);

			model.Rows = shown.ToList();
			return View(model);
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

		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> View(int id, int? versionId = null, bool preview = false)
		{
			var protocol = await _protocolsService.GetProtocolByIdAsync(id);
			if (protocol == null || protocol.DepartmentId != DepartmentId)
				return NotFound();

			var reader = await ReaderAsync();
			if (_access == null || !await _access.CanReadAsync(reader, protocol))
				return NotFound();

			var audience = _library == null ? new List<ProtocolAudience>() : await _library.GetAudienceAsync(DepartmentId, id);
			var versions = _library == null ? new List<ProtocolVersion>() : await _library.GetVersionsAsync(DepartmentId, id);
			var readableVersions = versions.Where(v => ProtocolAccessRules.CanReadVersion(reader, protocol, audience, v)).ToList();
			var current = protocol.CurrentVersionId.HasValue ? versions.FirstOrDefault(v => v.ProtocolVersionId == protocol.CurrentVersionId.Value) : null;
			var open = reader.IsManager ? versions.Where(v => v.Status == (int)ProtocolVersionStatuses.Draft || v.Status == (int)ProtocolVersionStatuses.InReview || v.Status == (int)ProtocolVersionStatuses.Approved)
				.OrderByDescending(v => v.ProtocolVersionId).FirstOrDefault() : null;

			var shownId = versionId ?? current?.ProtocolVersionId ?? open?.ProtocolVersionId;
			var shownHeader = shownId.HasValue ? readableVersions.FirstOrDefault(v => v.ProtocolVersionId == shownId.Value) : null;
			if (versionId.HasValue && shownHeader == null)
				return NotFound();
			var shown = shownHeader == null || _library == null ? null : await _library.GetVersionAsync(DepartmentId, shownHeader.ProtocolVersionId);

			var priorities = await _callsService.GetCallPrioritiesForDepartmentAsync(DepartmentId) ?? new List<DepartmentCallPriority>();
			var types = await _callsService.GetCallTypesForDepartmentAsync(DepartmentId) ?? new List<CallType>();
			var usage = await _protocolsService.GetUsageCountsAsync(DepartmentId, DateTime.UtcNow.AddDays(-30));
			var names = await NamesAsync();
			var (favorites, _) = _library == null ? (new List<int>(), new List<int>()) : await _library.GetFavoritesAndRecentAsync(DepartmentId, UserId);
			var owner = string.Equals(protocol.OwnerUserId, UserId, StringComparison.OrdinalIgnoreCase);

			var model = new ProtocolDetailModel
			{
				Protocol = protocol,
				Shown = shown,
				Current = current,
				Open = open,
				Versions = readableVersions,
				// P3: a block version's outline is its heading blocks (anchors b-{key}); P1 html keeps its heading anchors.
				Outline = ProtocolContentDocument.OutlineFor(shown?.ContentJson, shown?.ContentHtml ?? (shown == null ? protocol.ProtocolText : null)),
				TriggerLines = (protocol.Triggers ?? new List<DispatchProtocolTrigger>()).Select(t => ProtocolDisplay.Describe(t, priorities, types, _localizer)).ToList(),
				AudienceLabels = await AudienceLabelsAsync(audience),
				CallsLast30Days = usage.TryGetValue(protocol.DispatchProtocolId, out var calls) ? calls : 0,
				Names = names,
				CategoryName = protocol.CategoryId.HasValue && _library != null ? (await _library.GetCategoriesAsync(DepartmentId)).FirstOrDefault(c => c.ProtocolCategoryId == protocol.CategoryId.Value)?.Name : null,
				StatusKey = StatusKey(protocol, open),
				IsFavorite = favorites.Contains(protocol.DispatchProtocolId),
				IsManager = reader.IsManager && !preview,
				CanAuthor = reader.MayAuthor && !preview,
				CanApprove = reader.MayApprove && !preview,
				CanModify = !protocol.IsDeleted && reader.MayAuthor && !preview,
				CanDelete = !protocol.IsDeleted && HasProtocolClaim(ResgridClaimTypes.Actions.Delete) && !preview,
				CanRecordReview = !preview && !protocol.IsDeleted && protocol.IsPublished && (owner || reader.MayApprove),
				Preview = preview && reader.IsManager,
				Message = TempData?["ProtocolMessage"] as string,
				Error = TempData?["ProtocolError"] as string
			};

			if (model.IsManager && _library != null)
			{
				model.RecentCalls = await _library.GetRecentCallsAsync(DepartmentId, id);
				model.CallsByVersion = model.RecentCalls.Where(c => c.ProtocolVersionId.HasValue).GroupBy(c => c.ProtocolVersionId.Value).ToDictionary(g => g.Key, g => g.Count());
			}

			// Threads: managers see every suggestion and question; a member sees their own.
			if (_library != null)
			{
				var comments = await _library.GetCommentsAsync(DepartmentId, id);
				model.Threads = comments.Where(c => c.Kind != (int)ProtocolCommentKinds.ReviewComment &&
					(model.IsManager || string.Equals(c.AuthorUserId, UserId, StringComparison.OrdinalIgnoreCase) ||
					 (c.ParentCommentId.HasValue && comments.Any(p => p.ProtocolCommentId == c.ParentCommentId.Value && string.Equals(p.AuthorUserId, UserId, StringComparison.OrdinalIgnoreCase)))))
					.ToList();
			}

			if (_signatures != null && shown != null)
			{
				model.Signatures = new SignatureListModel { EmptyText = Signing("NoSignatures") };
				foreach (var kind in new[] { SignatureSubjectKinds.ProtocolVersion, SignatureSubjectKinds.ProtocolReview })
					model.Signatures.Items.AddRange(await _signatures.GetVerifiedForSubjectAsync(DepartmentId, kind, shown.ProtocolVersionId.ToString(CultureInfo.InvariantCulture)));
				model.Signatures.Items = model.Signatures.Items.OrderBy(s => s.Record.SignedOn).ToList();
			}

			if (model.CanRecordReview && current != null)
				model.ReviewPanel = await ReviewPanelAsync(protocol, current);

			if (_library != null && !preview)
				await _library.RecordViewAsync(DepartmentId, UserId, id);

			return View(model);
		}

		private async Task<List<string>> AudienceLabelsAsync(List<ProtocolAudience> audience)
		{
			var labels = new List<string>();
			if (audience == null || audience.Count == 0)
			{
				labels.Add(Text("AudienceEveryoneLabel"));
				return labels;
			}

			var lists = await EditorListsAsync();
			foreach (var rule in audience)
			{
				ContentAudienceMatcher.TryId(rule, out var refId);
				switch ((ContentAudienceKinds)rule.Kind)
				{
					case ContentAudienceKinds.Group: labels.Add(Text("AudienceGroupLabel") + ": " + (lists.Groups.TryGetValue(refId, out var g) ? g : rule.RefId)); break;
					case ContentAudienceKinds.Role: labels.Add(Text("AudienceRoleLabel") + ": " + (lists.Roles.TryGetValue(refId, out var r) ? r : rule.RefId)); break;
					case ContentAudienceKinds.Shift: labels.Add(Text("AudienceShiftLabel") + ": " + (lists.Shifts.TryGetValue(refId, out var s) ? s : rule.RefId)); break;
					case ContentAudienceKinds.UnitType: labels.Add(Text("AudienceUnitTypeLabel") + ": " + (lists.UnitTypes.TryGetValue(refId, out var u) ? u : rule.RefId)); break;
					case ContentAudienceKinds.CertificationType: labels.Add(Text("AudienceCertificationLabel") + ": " + (lists.CertificationTypes.TryGetValue(refId, out var c) ? c : rule.RefId)); break;
					default: labels.Add(rule.RefId); break;
				}
			}

			return labels;
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> Favorite(int id, bool favorite, string returnTo = null, CancellationToken cancellationToken = default)
		{
			if (_access == null || _library == null || await _access.GetReadableAsync(DepartmentId, UserId, id) == null)
				return NotFound();

			await _library.SetFavoriteAsync(DepartmentId, UserId, id, favorite, cancellationToken);
			return returnTo == "view" ? RedirectToAction("View", new { id }) : RedirectToAction("Index");
		}

		/// <summary>A member's suggestion or question from the reading view, a reply in a thread, or a reviewer's anchored comment.</summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> Comment(int id, int? versionId, int kind, string anchor, string body, int? parentId, string returnTo = null, CancellationToken cancellationToken = default)
		{
			if (_library == null)
				return NotFound();

			var saved = await _library.AddCommentAsync(await ReaderAsync(), id, versionId, (ProtocolCommentKinds)kind, anchor, body, parentId, cancellationToken);
			if (saved == null)
				TempData["ProtocolError"] = Text("CommentRefused");
			else
				TempData["ProtocolMessage"] = Text(kind == (int)ProtocolCommentKinds.ReviewComment ? "ReviewCommentSaved" : "SuggestionSaved");

			return returnTo == "review" && versionId.HasValue ? RedirectToAction("Review", new { id = versionId.Value }) : RedirectToAction("View", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> ResolveComment(int id, int commentId, int? versionId, string returnTo = null, CancellationToken cancellationToken = default)
		{
			if (_library == null)
				return NotFound();

			if (await _library.ResolveCommentAsync(await ReaderAsync(), commentId, cancellationToken) == null)
				TempData["ProtocolError"] = Text("CommentRefused");

			return returnTo == "review" && versionId.HasValue ? RedirectToAction("Review", new { id = versionId.Value }) : RedirectToAction("View", new { id });
		}

		#endregion Library and detail

		#region Creation wizard and editor

		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_Create)]
		public async Task<IActionResult> New(int? kind = null)
		{
			var settings = _library == null ? new ProtocolLibrarySettings() : await _library.GetSettingsAsync(DepartmentId);
			var model = new ProtocolEditorModel { IsNew = true };
			model.Library.Kind = kind.HasValue && ProtocolKindRules.IsAuthorable(kind.Value) ? kind.Value : (int)ProtocolKinds.Dispatch;
			model.Library.RequiresAcknowledgment = ProtocolKindRules.AcknowledgmentByDefault(model.Library.Kind);
			model.Library.ReviewIntervalDays = settings.DefaultReviewIntervalDays > 0 ? settings.DefaultReviewIntervalDays : (int?)null;
			model.Library.OwnerUserId = UserId;
			await FillEditorAsync(model);

			return View("New", model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_Create)]
		public async Task<IActionResult> New(ProtocolEditorModel model, ICollection<IFormFile> attachments, CancellationToken cancellationToken = default)
		{
			model ??= new ProtocolEditorModel();
			model.IsNew = true;
			model.Library ??= new ProtocolLibraryEditorFields();

			if (_library == null)
				return StatusCode(StatusCodes.Status503ServiceUnavailable);

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			var protocol = await BuildProtocolAsync(model, null, department);
			var uploads = await ReadUploadsAsync(attachments, 0, cancellationToken);

			// A new protocol is always a new row with new children: no id posted for it or its rows is used.
			protocol.DispatchProtocolId = 0;
			foreach (var trigger in protocol.Triggers) trigger.DispatchProtocolTriggerId = 0;
			foreach (var question in protocol.Questions)
			{
				question.DispatchProtocolQuestionId = 0;
				foreach (var answer in question.Answers) answer.DispatchProtocolQuestionAnswerId = 0;
			}

			protocol.Attachments = uploads;

			if (!ModelState.IsValid)
			{
				// The triggers and questions the author built are posted back into the page, not lost (D23).
				await FillEditorAsync(model);
				return View("New", model);
			}

			ApplyLibraryFields(protocol, model.Library);
			protocol.DepartmentId = DepartmentId;
			protocol.CreatedOn = DateTime.UtcNow;
			protocol.CreatedByUserId = UserId;
			protocol.UpdatedOn = DateTime.UtcNow;
			protocol.UpdatedByUserId = UserId;

			var created = await _library.CreateAsync(new ProtocolCreateRequest
			{
				DepartmentId = DepartmentId,
				Protocol = protocol,
				ContentHtml = model.ProtocolText,
				// P3: a new protocol's draft is a block document; the wizard's text (if any) is converted to blocks.
				AsBlocks = true,
				RequiresAcknowledgment = model.Library.RequiresAcknowledgment,
				Audience = AudienceRules(model.Library),
				UserId = UserId
			}, cancellationToken);

			if (!created.Succeeded)
			{
				ModelState.AddModelError("", LifecycleError(created.Error));
				await FillEditorAsync(model);
				return View("New", model);
			}

			if (!string.IsNullOrWhiteSpace(model.Library.ChangeSummary))
				await _library.SaveDraftAsync(await ReaderAsync(), created.Protocol.DispatchProtocolId, new ProtocolDraftInput
				{
					ContentHtml = created.Version.ContentHtml,
					ContentJson = created.Version.ContentJson,
					ChangeSummary = model.Library.ChangeSummary,
					RequiresAcknowledgment = created.Version.RequiresAcknowledgment,
					ExpectedRowVersion = created.Version.RowVersion
				}, cancellationToken);

			TempData["ProtocolMessage"] = Text("ProtocolCreatedMessage");
			return RedirectToAction("Edit", new { id = created.Protocol.DispatchProtocolId });
		}

		/// <summary>"Start from template": a new draft protocol copied from one the author can read.</summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_Create)]
		public async Task<IActionResult> Copy(int id, CancellationToken cancellationToken = default)
		{
			if (_library == null)
				return NotFound();

			var copied = await _library.CopyAsync(await ReaderAsync(), id, cancellationToken);
			if (!copied.Succeeded)
			{
				TempData["ProtocolError"] = LifecycleError(copied.Error);
				return RedirectToAction("Index");
			}

			TempData["ProtocolMessage"] = Text("ProtocolCopiedMessage");
			return RedirectToAction("Edit", new { id = copied.Protocol.DispatchProtocolId });
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_Update)]
		public async Task<IActionResult> Edit(int id)
		{
			if (!await _authorizationService.CanUserModifyProtocolAsync(UserId, id))
				return Unauthorized();

			var protocol = await _protocolsService.GetProtocolByIdAsync(id);
			if (protocol == null)
				return NotFound();

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			var time = new DepartmentTime(department);
			var draft = _library == null ? null : await _library.GetOpenDraftAsync(DepartmentId, id);
			var current = protocol.CurrentVersionId.HasValue && _library != null ? await _library.GetVersionAsync(DepartmentId, protocol.CurrentVersionId.Value) : null;
			var audience = _library == null ? new List<ProtocolAudience>() : await _library.GetAudienceAsync(DepartmentId, id);

			var model = new ProtocolEditorModel
			{
				ProtocolId = protocol.DispatchProtocolId,
				Name = protocol.Name,
				Code = protocol.Code,
				Description = protocol.Description,
				// The editor works on the draft; with none yet, it starts from the version in force (saving creates the draft).
				// P4: without the rendered case flow, which is edited in the builder and re-rendered from the graph.
				ProtocolText = ProtocolDecisionGraphs.StripRendered(draft?.ContentHtml ?? current?.ContentHtml ?? protocol.ProtocolText),
				// P3: a block version is edited in the block editor (its document); an html one keeps the html editor until "Convert to blocks".
				ContentJson = BlockJson(draft ?? current),
				// P4: the case flow of the draft (or the version in force until a draft exists).
				DecisionGraphJson = GraphJson(draft ?? current),
				MinimumWeight = protocol.MinimumWeight,
				IsDisabled = protocol.IsDisabled,
				RowVersion = protocol.RowVersion,
				DraftRowVersion = draft?.RowVersion ?? 0,
				Library = new ProtocolLibraryEditorFields
				{
					Kind = protocol.Kind,
					CategoryId = protocol.CategoryId,
					Number = protocol.Number,
					Tags = protocol.Tags,
					OwnerUserId = protocol.OwnerUserId,
					ReviewIntervalDays = protocol.ReviewIntervalDays,
					ExcludeFromAi = protocol.ExcludeFromAi,
					IsLicensedContent = protocol.IsLicensedContent,
					LegalHold = protocol.LegalHold,
					RequiresAcknowledgment = draft?.RequiresAcknowledgment ?? current?.RequiresAcknowledgment ?? ProtocolKindRules.AcknowledgmentByDefault(protocol.Kind),
					ChangeSummary = draft?.ChangeSummary,
					AudienceEveryone = audience.Count == 0,
					AudienceGroups = Ids(audience, ContentAudienceKinds.Group),
					AudienceRoles = Ids(audience, ContentAudienceKinds.Role),
					AudienceShifts = Ids(audience, ContentAudienceKinds.Shift),
					AudienceUnitTypes = Ids(audience, ContentAudienceKinds.UnitType),
					AudienceCertificationTypes = Ids(audience, ContentAudienceKinds.CertificationType)
				},
				Triggers = protocol.Triggers.Select(t => new ProtocolTriggerInput
				{
					Key = "t" + t.DispatchProtocolTriggerId,
					Id = t.DispatchProtocolTriggerId,
					Type = t.Type,
					StartsOn = time.Input(t.StartsOn),
					EndsOn = time.Input(t.EndsOn),
					Priority = t.Priority,
					CallTypeId = t.CallTypeId,
					CallTypeName = t.CallTypeId.HasValue || ProtocolTriggerMatcher.IsNoTypeTrigger(t) ? null : t.CallType,
					GeofenceGroupId = t.GeofenceGroupId,
					Days = Enumerable.Range(0, 7).Where(d => t.DaysOfWeek.HasValue && t.DaysOfWeek.Value > 0 && (t.DaysOfWeek.Value & (1 << d)) != 0).ToList(),
					StartTime = t.StartMinute.HasValue ? $"{t.StartMinute.Value / 60:00}:{t.StartMinute.Value % 60:00}" : null,
					EndTime = t.EndMinute.HasValue ? (t.EndMinute.Value >= DispatchTriggerEngine.MinutesPerDay ? "23:59" : $"{t.EndMinute.Value / 60:00}:{t.EndMinute.Value % 60:00}") : null,
					Keywords = t.Keywords,
					PoiTypeId = t.PoiTypeId,
					CallSources = DispatchTriggerEngine.ParseCallSources(t.CallSources).OrderBy(s => s).ToList()
				}).ToList(),
				Questions = protocol.Questions.Select(q => new ProtocolQuestionInput
				{
					Key = "q" + q.DispatchProtocolQuestionId,
					Id = q.DispatchProtocolQuestionId,
					Text = q.Question,
					Answers = (q.Answers ?? Enumerable.Empty<DispatchProtocolQuestionAnswer>()).Select(a => new ProtocolAnswerInput
					{
						Key = "a" + a.DispatchProtocolQuestionAnswerId,
						Id = a.DispatchProtocolQuestionAnswerId,
						Text = a.Answer,
						Weight = a.Weight
					}).ToList()
				}).ToList(),
				Message = TempData?["ProtocolMessage"] as string
			};

			await FillEditorAsync(model, protocol, draft, current);
			return View("Edit", model);
		}

		/// <summary>P4: "HH:mm" as minutes after midnight; an end of 00:00 or 23:59 is the end of the day; null for blank or unreadable.</summary>
		private static int? MinuteOfDay(string value, bool isEnd)
		{
			if (string.IsNullOrWhiteSpace(value) || !TimeSpan.TryParse(value.Trim(), System.Globalization.CultureInfo.InvariantCulture, out var time) || time < TimeSpan.Zero || time.TotalMinutes >= DispatchTriggerEngine.MinutesPerDay)
				return null;

			var minutes = (int)time.TotalMinutes;
			if (isEnd && (minutes == 0 || minutes == DispatchTriggerEngine.MinutesPerDay - 1))
				return DispatchTriggerEngine.MinutesPerDay;
			return minutes;
		}

		/// <summary>P4: the version's decision graph as the builder edits it (ContentJson "graph"), or null when it has none.</summary>
		private static string GraphJson(ProtocolVersion version)
		{
			var graph = version == null || version.IsDocument ? null : ProtocolContentDocument.Parse(version.ContentJson, null).Graph;
			return graph == null || graph.IsEmpty ? null : graph.ToJson();
		}

		/// <summary>The version's block document as JSON when it is one (P3), else null (html and document-backed versions).</summary>
		private static string BlockJson(ProtocolVersion version) =>
			version == null || version.IsDocument || !ProtocolContentDocument.Parse(version.ContentJson, null).IsBlockModel ? null : version.ContentJson;

		private static List<int> Ids(IEnumerable<ProtocolAudience> audience, ContentAudienceKinds kind) =>
			audience.Where(a => a.Kind == (int)kind).Select(a => ContentAudienceMatcher.TryId(a, out var id) ? id : 0).Where(id => id > 0).ToList();

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_Update)]
		public async Task<IActionResult> Edit(int id, ProtocolEditorModel model, ICollection<IFormFile> attachments, CancellationToken cancellationToken = default)
		{
			if (!await _authorizationService.CanUserModifyProtocolAsync(UserId, id))
				return Unauthorized();

			var current = await _protocolsService.GetProtocolByIdAsync(id);
			if (current == null || current.DepartmentId != DepartmentId)
				return NotFound();

			model ??= new ProtocolEditorModel();
			model.ProtocolId = id;
			model.Library ??= new ProtocolLibraryEditorFields();

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			var edited = await BuildProtocolAsync(model, current, department);
			edited.DispatchProtocolId = id;
			edited.DepartmentId = DepartmentId;

			// Overposting: a child id must be one of this protocol's own rows. Anything else is refused outright.
			if (ProtocolRules.ForeignChildIds(current, edited).Count > 0)
				return BadRequest();

			var removeIds = (model.RemoveAttachmentIds ?? new List<int>()).Distinct().ToList();
			if (removeIds.Any(x => current.Attachments.All(a => a.DispatchProtocolAttachmentId != x)))
				return BadRequest();

			var remaining = current.Attachments.Count(a => !removeIds.Contains(a.DispatchProtocolAttachmentId));
			edited.Attachments = await ReadUploadsAsync(attachments, remaining, cancellationToken);

			var draft = _library == null ? null : await _library.GetOpenDraftAsync(DepartmentId, id);
			var names = await NamesAsync();

			// Co-editing (R-P8): a save against an older copy of the protocol or its draft is refused, with who changed it and when.
			if (current.RowVersion != model.RowVersion)
				ModelState.AddModelError("", string.Format(Text("StaleProtocolMessage"), NameOf(names, current.UpdatedByUserId), new DepartmentTime(department).Format(current.UpdatedOn)));
			else if (draft != null && draft.RowVersion != model.DraftRowVersion)
				ModelState.AddModelError("", string.Format(Text("StaleProtocolMessage"), NameOf(names, draft.UpdatedByUserId), new DepartmentTime(department).Format(draft.UpdatedOn)));

			if (!ModelState.IsValid)
			{
				await FillEditorAsync(model, current, draft, null);
				return View("Edit", model);
			}

			var reader = await ReaderAsync();
			var before = Snapshot(current);

			if (_library != null)
			{
				var fields = await _library.UpdateLibraryFieldsAsync(reader, id, LibraryFields(model.Library), model.RowVersion, cancellationToken);
				if (!fields.Succeeded)
				{
					ModelState.AddModelError("", fields.Error == ProtocolLifecycleErrors.StaleCopy
						? string.Format(Text("StaleProtocolMessage"), NameOf(names, fields.ConflictUserId), new DepartmentTime(department).Format(fields.ConflictOn))
						: LifecycleError(fields.Error));
					await FillEditorAsync(model, current, draft, null);
					return View("Edit", model);
				}
			}

			// Kinds other than Dispatch and Procedure are never matched to calls: their triggers and questions are cleared.
			if (!ProtocolKindRules.TriggersApply(model.Library.Kind))
			{
				edited.Triggers = new List<DispatchProtocolTrigger>();
				edited.Questions = new List<DispatchProtocolQuestion>();
			}

			var saved = await _protocolsService.UpdateProtocolAsync(edited, removeIds, UserId, cancellationToken);
			Audit(AuditLogTypes.ProtocolUpdated, before, saved);
			if (current.IsDisabled != saved.IsDisabled)
				Audit(saved.IsDisabled ? AuditLogTypes.ProtocolDisabled : AuditLogTypes.ProtocolEnabled, before, saved);

			if (_library != null)
			{
				await _library.SetAudienceAsync(reader, id, AudienceRules(model.Library), cancellationToken);

				// The content goes to the draft when it changed or a draft is open; an unchanged text with no draft creates none.
				var currentVersion = current.CurrentVersionId.HasValue ? await _library.GetVersionAsync(DepartmentId, current.CurrentVersionId.Value) : null;
				var blocks = !string.IsNullOrWhiteSpace(model.ContentJson);
				var settings = await _library.GetSettingsAsync(DepartmentId);
				var postedDocument = blocks ? ProtocolBlocks.Normalize(ProtocolContentDocument.Parse(model.ContentJson, null))
					: ProtocolContentDocument.FromHtml(ProtocolDecisionGraphs.StripRendered(model.ProtocolText));
				// P4: the case flow is content too: the builder's graph (blank removes it), or the one in force when the builder was not posted.
				var locale = settings.ContentLocaleFor(draft ?? currentVersion);
				postedDocument.Graph = model.Library.Kind != (int)ProtocolKinds.Dispatch ? null
					: model.DecisionGraphJson != null ? ProtocolDecisionGraphs.Normalize(ProtocolDecisionGraph.Parse(model.DecisionGraphJson))
					: ProtocolContentDocument.Parse((draft ?? currentVersion)?.ContentJson, null).Graph;
				postedDocument.GraphLabels = Resgrid.Services.ProtocolBlockLabelsProvider.GraphFor(locale);
				var posted = postedDocument.Render(Resgrid.Services.ProtocolBlockLabelsProvider.For(locale));
				var changed = !string.Equals(ProtocolContentHash.Compute(posted), currentVersion?.ContentHash ?? ProtocolContentHash.Compute(current.ProtocolText), StringComparison.Ordinal);
				if ((draft != null && draft.Status == (int)ProtocolVersionStatuses.Draft) || (draft == null && (changed || model.Command == "submit")))
				{
					var outcome = await _library.SaveDraftAsync(reader, id, new ProtocolDraftInput
					{
						ContentHtml = model.ProtocolText,
						ContentJson = blocks ? model.ContentJson : null,
						DecisionGraphJson = model.Library.Kind == (int)ProtocolKinds.Dispatch ? model.DecisionGraphJson : null,
						ChangeSummary = model.Library.ChangeSummary,
						RequiresAcknowledgment = model.Library.RequiresAcknowledgment,
						ExpectedRowVersion = draft?.RowVersion ?? 0
					}, cancellationToken);

					if (!outcome.Succeeded)
					{
						TempData["ProtocolError"] = outcome.Error == ProtocolLifecycleErrors.StaleCopy
							? string.Format(Text("StaleProtocolMessage"), NameOf(names, outcome.ConflictUserId), new DepartmentTime(department).Format(outcome.ConflictOn))
							: LifecycleError(outcome.Error);
						return RedirectToAction("Edit", new { id });
					}

					draft = outcome.Version;
				}

				if (model.Command == "submit" && draft != null)
				{
					var submitted = await _library.SubmitAsync(reader, draft.ProtocolVersionId, model.Library.ChangeSummary, cancellationToken);
					if (!submitted.Succeeded)
					{
						TempData["ProtocolError"] = LifecycleError(submitted.Error);
						return RedirectToAction("Edit", new { id });
					}

					TempData["ProtocolMessage"] = Text("SubmittedMessage");
					return RedirectToAction("View", new { id });
				}
			}

			TempData["ProtocolMessage"] = Text("ProtocolSavedMessage");
			return RedirectToAction("View", new { id });
		}

		/// <summary>Autosave (R-P8): the draft's content only, refused with who and when on a stale copy. Returns the new row version.</summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_Update)]
		public async Task<IActionResult> SaveDraft(int id, string protocolText, string changeSummary, bool requiresAcknowledgment, long draftRowVersion,
			CancellationToken cancellationToken = default, string contentJson = null, string decisionGraphJson = null)
		{
			if (_library == null)
				return NotFound();

			var outcome = await _library.SaveDraftAsync(await ReaderAsync(), id, new ProtocolDraftInput
			{
				ContentHtml = protocolText,
				ContentJson = string.IsNullOrWhiteSpace(contentJson) ? null : contentJson,
				// P4: null (no builder on the page) keeps the draft's graph.
				DecisionGraphJson = decisionGraphJson,
				ChangeSummary = changeSummary,
				RequiresAcknowledgment = requiresAcknowledgment,
				ExpectedRowVersion = draftRowVersion
			}, cancellationToken);

			if (outcome.Succeeded)
				return Json(new { success = true, rowVersion = outcome.Version.RowVersion, versionId = outcome.Version.ProtocolVersionId, savedOn = DateTime.UtcNow });

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			var message = outcome.Error == ProtocolLifecycleErrors.StaleCopy
				? string.Format(Text("StaleProtocolMessage"), NameOf(await NamesAsync(), outcome.ConflictUserId), new DepartmentTime(department).Format(outcome.ConflictOn))
				: LifecycleError(outcome.Error);
			return Json(new { success = false, error = outcome.Error.ToString(), message });
		}

		/// <summary>
		/// "Convert to blocks" (P3): the open draft's html becomes a block document (a draft is opened from the version in force first).
		/// Only drafts change: the published versions, their HTML and their hashes stay as they are.
		/// </summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_Update)]
		public async Task<IActionResult> ConvertToBlocks(int id, long draftRowVersion, CancellationToken cancellationToken = default)
		{
			if (_library == null)
				return NotFound();

			var reader = await ReaderAsync();
			var draft = await _library.GetOpenDraftAsync(DepartmentId, id);
			if (draft == null)
			{
				var started = await _library.StartDraftAsync(reader, id, cancellationToken);
				if (!started.Succeeded)
				{
					TempData["ProtocolError"] = LifecycleError(started.Error);
					return RedirectToAction("Edit", new { id });
				}
				draft = started.Version;
				draftRowVersion = draft.RowVersion;
			}

			var outcome = await _library.ConvertDraftToBlocksAsync(reader, id, draftRowVersion, cancellationToken);
			if (outcome.Succeeded)
				TempData["ProtocolMessage"] = Text("ConvertedToBlocksMessage");
			else if (outcome.Error == ProtocolLifecycleErrors.StaleCopy)
				TempData["ProtocolError"] = string.Format(Text("StaleProtocolMessage"), NameOf(await NamesAsync(), outcome.ConflictUserId),
					new DepartmentTime(await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false)).Format(outcome.ConflictOn));
			else
				TempData["ProtocolError"] = LifecycleError(outcome.Error);
			return RedirectToAction("Edit", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_Update)]
		public async Task<IActionResult> StartDraft(int id, CancellationToken cancellationToken = default)
		{
			if (_library == null)
				return NotFound();

			var outcome = await _library.StartDraftAsync(await ReaderAsync(), id, cancellationToken);
			if (!outcome.Succeeded && outcome.Error != ProtocolLifecycleErrors.DraftExists)
				TempData["ProtocolError"] = LifecycleError(outcome.Error);

			return RedirectToAction("Edit", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_Update)]
		public async Task<IActionResult> DiscardDraft(int id, int versionId, CancellationToken cancellationToken = default)
		{
			if (_library == null)
				return NotFound();

			var outcome = await _library.DiscardDraftAsync(await ReaderAsync(), versionId, cancellationToken);
			TempData[outcome.Succeeded ? "ProtocolMessage" : "ProtocolError"] = outcome.Succeeded ? Text("DraftDiscardedMessage") : LifecycleError(outcome.Error);
			return RedirectToAction("View", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_Update)]
		public async Task<IActionResult> Submit(int id, int versionId, string changeSummary, CancellationToken cancellationToken = default)
		{
			if (_library == null)
				return NotFound();

			var outcome = await _library.SubmitAsync(await ReaderAsync(), versionId, changeSummary, cancellationToken);
			TempData[outcome.Succeeded ? "ProtocolMessage" : "ProtocolError"] = outcome.Succeeded ? Text("SubmittedMessage") : LifecycleError(outcome.Error);
			return RedirectToAction("View", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> Withdraw(int id, int versionId, CancellationToken cancellationToken = default)
		{
			if (_library == null)
				return NotFound();

			var outcome = await _library.WithdrawAsync(await ReaderAsync(), versionId, cancellationToken);
			TempData[outcome.Succeeded ? "ProtocolMessage" : "ProtocolError"] = outcome.Succeeded ? Text("WithdrawnMessage") : LifecycleError(outcome.Error);
			return RedirectToAction("View", new { id });
		}

		/// <summary>Turns a posted editor into a protocol: trimmed fields, triggers in UTC, call types by id, questions and answers.</summary>
		private async Task<DispatchProtocol> BuildProtocolAsync(ProtocolEditorModel model, DispatchProtocol current, Department department)
		{
			var time = new DepartmentTime(department);
			var priorities = await _callsService.GetCallPrioritiesForDepartmentAsync(DepartmentId) ?? new List<DepartmentCallPriority>();
			var types = await _callsService.GetCallTypesForDepartmentAsync(DepartmentId) ?? new List<CallType>();

			var protocol = new DispatchProtocol
			{
				Name = model.Name?.Trim(),
				Code = ProtocolRules.NormalizeCode(model.Code),
				Description = model.Description,
				ProtocolText = model.ProtocolText,
				MinimumWeight = model.MinimumWeight,
				IsDisabled = model.IsDisabled,
				Triggers = new List<DispatchProtocolTrigger>(),
				Questions = new List<DispatchProtocolQuestion>()
			};

			foreach (var input in model.Triggers ?? new List<ProtocolTriggerInput>())
			{
				if (input == null)
					continue;

				var trigger = new DispatchProtocolTrigger { DispatchProtocolTriggerId = input.Id, Type = input.Type };

				if (!string.IsNullOrWhiteSpace(input.StartsOn))
				{
					trigger.StartsOn = time.Parse(input.StartsOn);
					if (!trigger.StartsOn.HasValue) ModelState.AddModelError("Triggers", Text("ErrorTriggerDate"));
				}

				if (!string.IsNullOrWhiteSpace(input.EndsOn))
				{
					trigger.EndsOn = time.Parse(input.EndsOn);
					if (!trigger.EndsOn.HasValue) ModelState.AddModelError("Triggers", Text("ErrorTriggerDate"));
				}

				var needsPriority = input.Type == (int)ProtocolTriggerTypes.CallPriorty || input.Type == (int)ProtocolTriggerTypes.CallPriortyAndType;
				var needsType = input.Type == (int)ProtocolTriggerTypes.CallType || input.Type == (int)ProtocolTriggerTypes.CallPriortyAndType;

				if (needsPriority)
				{
					// The department's own priority, or the one the trigger already had (a priority removed since stays visible).
					var existing = current?.Triggers?.FirstOrDefault(x => x.DispatchProtocolTriggerId == input.Id);
					if (input.Priority.HasValue && (priorities.Any(x => x.DepartmentCallPriorityId == input.Priority.Value && !x.IsDeleted) || existing?.Priority == input.Priority))
						trigger.Priority = input.Priority;
					else
						ModelState.AddModelError("Triggers", Text("ErrorTriggerPriority"));
				}

				if (needsType)
				{
					if (input.CallTypeId.HasValue)
					{
						var type = types.FirstOrDefault(x => x.CallTypeId == input.CallTypeId.Value);
						var existing = current?.Triggers?.FirstOrDefault(x => x.DispatchProtocolTriggerId == input.Id);

						if (type != null)
						{
							trigger.CallTypeId = type.CallTypeId;
							trigger.CallType = type.Type;
						}
						else if (existing != null && existing.CallTypeId == input.CallTypeId)
						{
							// A type deleted since: kept as it was, so the author sees it and can change it.
							trigger.CallTypeId = existing.CallTypeId;
							trigger.CallType = existing.CallType;
						}
						else
						{
							ModelState.AddModelError("Triggers", Text("ErrorTriggerCallType"));
						}
					}
					else if (!string.IsNullOrWhiteSpace(input.CallTypeName) && current?.Triggers?.Any(x => x.DispatchProtocolTriggerId == input.Id && x.CallType == input.CallTypeName) == true)
					{
						// A trigger written before call type ids whose type no longer exists: its name, unchanged.
						trigger.CallType = input.CallTypeName;
					}
					// else: "no type" (null id, no name).
				}

				// P4 conditions (shared trigger engine); ids the department does not have are refused.
				if (input.GeofenceGroupId.HasValue)
				{
					if (_groups != null && ((await _groups.GetAllGroupsForDepartmentUnlimitedAsync(DepartmentId)) ?? new List<DepartmentGroup>()).Any(g => g.DepartmentGroupId == input.GeofenceGroupId.Value))
						trigger.GeofenceGroupId = input.GeofenceGroupId;
					else
						ModelState.AddModelError("Triggers", Text("ErrorTriggerConditions"));
				}

				if (input.PoiTypeId.HasValue)
				{
					if (_mappingService != null && ((await _mappingService.GetPOITypesForDepartmentAsync(DepartmentId)) ?? new List<PoiType>()).Any(p => p.PoiTypeId == input.PoiTypeId.Value))
						trigger.PoiTypeId = input.PoiTypeId;
					else
						ModelState.AddModelError("Triggers", Text("ErrorTriggerConditions"));
				}

				var days = (input.Days ?? new List<int>()).Where(d => d >= 0 && d < 7).Distinct().Aggregate(0, (mask, d) => mask | (1 << d));
				trigger.DaysOfWeek = days == 0 || days == DispatchTriggerEngine.EveryDay ? (int?)null : days;
				trigger.StartMinute = MinuteOfDay(input.StartTime, false);
				trigger.EndMinute = MinuteOfDay(input.EndTime, true);
				trigger.Keywords = DispatchTriggerEngine.NormalizeKeywords(input.Keywords);
				trigger.CallSources = DispatchTriggerEngine.NormalizeCallSources(input.CallSources);

				if (DispatchTriggerEngine.Validate(trigger.Type, trigger).Count > 0)
					ModelState.AddModelError("Triggers", Text("ErrorTriggerConditions"));

				protocol.Triggers.Add(trigger);
			}

			foreach (var input in model.Questions ?? new List<ProtocolQuestionInput>())
			{
				if (input == null)
					continue;

				var answers = (input.Answers ?? new List<ProtocolAnswerInput>()).Where(a => a != null && (a.Id > 0 || !string.IsNullOrWhiteSpace(a.Text))).ToList();

				// An untouched blank row the author added and never filled in is not a question.
				if (input.Id <= 0 && string.IsNullOrWhiteSpace(input.Text) && answers.Count == 0)
					continue;

				protocol.Questions.Add(new DispatchProtocolQuestion
				{
					DispatchProtocolQuestionId = input.Id,
					Question = input.Text?.Trim(),
					Answers = answers.Select(a => new DispatchProtocolQuestionAnswer
					{
						DispatchProtocolQuestionAnswerId = a.Id,
						Answer = a.Text?.Trim(),
						Weight = a.Weight
					}).ToList()
				});
			}

			foreach (var error in ProtocolRules.Validate(protocol))
				ModelState.AddModelError(error == "ErrorDescriptionTooLong" ? "Description" : error.StartsWith("ErrorCode") ? "Code" : "", Text(error));

			if (model.Library != null && !ProtocolKindRules.IsAuthorable(model.Library.Kind))
				ModelState.AddModelError("Library.Kind", Text("ErrorKind"));

			return protocol;
		}

		private static void ApplyLibraryFields(DispatchProtocol protocol, ProtocolLibraryEditorFields fields)
		{
			fields ??= new ProtocolLibraryEditorFields();
			protocol.Kind = ProtocolKindRules.IsAuthorable(fields.Kind) ? fields.Kind : (int)ProtocolKinds.Dispatch;
			protocol.CategoryId = fields.CategoryId;
			protocol.Number = string.IsNullOrWhiteSpace(fields.Number) ? null : fields.Number.Trim();
			protocol.Tags = fields.Tags;
			protocol.OwnerUserId = fields.OwnerUserId;
			protocol.ReviewIntervalDays = fields.ReviewIntervalDays;
			protocol.ExcludeFromAi = fields.ExcludeFromAi;
			protocol.IsLicensedContent = fields.IsLicensedContent;
		}

		private static ProtocolLibraryFields LibraryFields(ProtocolLibraryEditorFields fields) => new ProtocolLibraryFields
		{
			Kind = fields.Kind,
			CategoryId = fields.CategoryId,
			Number = fields.Number,
			Tags = fields.Tags,
			OwnerUserId = fields.OwnerUserId,
			ReviewIntervalDays = fields.ReviewIntervalDays,
			ExcludeFromAi = fields.ExcludeFromAi,
			IsLicensedContent = fields.IsLicensedContent,
			LegalHold = fields.LegalHold
		};

		private static List<ContentAudienceRule> AudienceRules(ProtocolLibraryEditorFields fields)
		{
			var rules = new List<ContentAudienceRule>();
			if (fields == null || fields.AudienceEveryone)
				return rules;

			void Add(IEnumerable<int> ids, ContentAudienceKinds kind)
			{
				foreach (var id in (ids ?? Enumerable.Empty<int>()).Where(x => x > 0).Distinct())
					rules.Add(new ContentAudienceRule(kind, id.ToString(CultureInfo.InvariantCulture)));
			}

			Add(fields.AudienceGroups, ContentAudienceKinds.Group);
			Add(fields.AudienceRoles, ContentAudienceKinds.Role);
			Add(fields.AudienceShifts, ContentAudienceKinds.Shift);
			Add(fields.AudienceUnitTypes, ContentAudienceKinds.UnitType);
			Add(fields.AudienceCertificationTypes, ContentAudienceKinds.CertificationType);
			return rules;
		}

		/// <summary>
		/// The uploaded files, each read whole (CopyTo a buffer, the stream opened once), within the per-file, per-save and
		/// per-protocol limits and the allowed types.
		/// </summary>
		private async Task<List<DispatchProtocolAttachment>> ReadUploadsAsync(ICollection<IFormFile> files, int existingCount, CancellationToken cancellationToken)
		{
			var uploads = new List<DispatchProtocolAttachment>();
			var posted = (files ?? new List<IFormFile>()).Where(x => x != null && x.Length > 0).ToList();

			if (posted.Count == 0)
				return uploads;

			if (posted.Count > ProtocolRules.MaxFilesPerSave)
				ModelState.AddModelError("attachments", string.Format(Text("ErrorTooManyFiles"), ProtocolRules.MaxFilesPerSave));

			if (existingCount + posted.Count > ProtocolRules.MaxAttachmentsPerProtocol)
				ModelState.AddModelError("attachments", string.Format(Text("ErrorTooManyAttachments"), ProtocolRules.MaxAttachmentsPerProtocol));

			if (posted.Sum(x => x.Length) > ProtocolRules.MaxBytesPerSave)
				ModelState.AddModelError("attachments", string.Format(Text("ErrorUploadTooLarge"), ProtocolRules.MaxBytesPerSave / 1_000_000));

			foreach (var file in posted)
			{
				if (!ProtocolRules.IsAllowedFile(file.FileName))
				{
					ModelState.AddModelError("attachments", string.Format(Text("ErrorFileType"), FileHelper.GetFileExtensionWithoutDot(file.FileName)));
					continue;
				}

				if (file.Length > ProtocolRules.MaxAttachmentBytes)
				{
					ModelState.AddModelError("attachments", string.Format(Text("ErrorFileTooLarge"), file.FileName, ProtocolRules.MaxAttachmentBytes / 1_000_000));
					continue;
				}

				if (!ModelState.IsValid)
					continue;

				using var buffer = new MemoryStream();
				await file.CopyToAsync(buffer, cancellationToken);

				uploads.Add(new DispatchProtocolAttachment
				{
					FileName = Path.GetFileName(file.FileName),
					FileType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
					Data = buffer.ToArray()
				});
			}

			return uploads;
		}

		private async Task<ProtocolEditorLists> EditorListsAsync()
		{
			var lists = new ProtocolEditorLists();
			if (_library != null)
				lists.Categories = await _library.GetCategoriesAsync(DepartmentId);
			foreach (var person in await _departmentsService.GetSelectablePersonnelNamesAsync(DepartmentId) ?? new List<PersonName>())
				if (person != null && !string.IsNullOrWhiteSpace(person.UserId))
					lists.Members[person.UserId] = person.Name?.Trim();
			if (_groups != null)
				foreach (var g in await _groups.GetAllGroupsForDepartmentAsync(DepartmentId) ?? new List<DepartmentGroup>())
					lists.Groups[g.DepartmentGroupId] = g.Name;
			if (_roles != null)
				foreach (var r in await _roles.GetRolesForDepartmentAsync(DepartmentId) ?? new List<PersonnelRole>())
					lists.Roles[r.PersonnelRoleId] = r.Name;
			if (_shifts != null)
				foreach (var s in await _shifts.GetAllShiftsByDepartmentAsync(DepartmentId) ?? new List<Shift>())
					lists.Shifts[s.ShiftId] = s.Name;
			if (_units != null)
				foreach (var u in await _units.GetUnitTypesForDepartmentAsync(DepartmentId) ?? new List<UnitType>())
					lists.UnitTypes[u.UnitTypeId] = u.Type;
			if (_certifications != null)
				foreach (var c in await _certifications.GetAllCertificationTypesByDepartmentAsync(DepartmentId) ?? new List<DepartmentCertificationType>())
					if (!c.IsDeleted)
						lists.CertificationTypes[c.DepartmentCertificationTypeId] = c.Type;
			return lists;
		}

		private async Task FillEditorAsync(ProtocolEditorModel model, DispatchProtocol current = null, ProtocolVersion draft = null, ProtocolVersion currentVersion = null)
		{
			model.CallPriorities = await _callsService.GetCallPrioritiesForDepartmentAsync(DepartmentId) ?? new List<DepartmentCallPriority>();
			model.CallTypes = await _callsService.GetCallTypesForDepartmentAsync(DepartmentId) ?? new List<CallType>();
			// P4 trigger conditions: zones are groups with a geofence (drawn once on the group), occupancy types are POI types.
			model.GeofenceGroups = _groups == null ? new List<DepartmentGroup>()
				: ((await _groups.GetAllGroupsForDepartmentUnlimitedAsync(DepartmentId)) ?? new List<DepartmentGroup>()).Where(g => g != null && !string.IsNullOrWhiteSpace(g.Geofence)).OrderBy(g => g.Name).ToList();
			model.PoiTypes = _mappingService == null ? new List<PoiType>() : (await _mappingService.GetPOITypesForDepartmentAsync(DepartmentId) ?? new List<PoiType>()).OrderBy(p => p.Name).ToList();
			model.ExistingAttachments = current?.Attachments?.ToList() ?? new List<DispatchProtocolAttachment>();
			model.Triggers ??= new List<ProtocolTriggerInput>();
			model.Questions ??= new List<ProtocolQuestionInput>();
			model.RemoveAttachmentIds ??= new List<int>();
			model.Library ??= new ProtocolLibraryEditorFields();
			model.Lists = await EditorListsAsync();
			model.CanApprove = (await ReaderAsync()).MayApprove;

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			model.TimeZoneName = department?.TimeZone;

			if (draft != null)
			{
				model.DraftVersionId = draft.ProtocolVersionId;
				model.DraftStatus = draft.Status;
				model.DraftUpdatedBy = NameOf(await NamesAsync(), draft.UpdatedByUserId);
				model.DraftUpdatedOn = draft.UpdatedOn;
				model.ContentLocked = !ProtocolLifecycle.IsContentEditable(draft.Status);
				if (model.ContentLocked)
					model.ProtocolText = draft.ContentHtml;
			}

			if (current?.CurrentVersionId != null && _library != null)
				model.CurrentVersionLabel = (currentVersion ?? await _library.GetVersionAsync(DepartmentId, current.CurrentVersionId.Value))?.Label;

			// Rows posted back after a failed save keep the keys they had; rows built server side get one.
			var index = 0;
			foreach (var trigger in model.Triggers)
				trigger.Key = string.IsNullOrWhiteSpace(trigger.Key) ? "t" + (trigger.Id > 0 ? trigger.Id.ToString() : "n" + index++) : trigger.Key;
			foreach (var question in model.Questions)
			{
				question.Key = string.IsNullOrWhiteSpace(question.Key) ? "q" + (question.Id > 0 ? question.Id.ToString() : "n" + index++) : question.Key;
				question.Answers ??= new List<ProtocolAnswerInput>();
				foreach (var answer in question.Answers)
					answer.Key = string.IsNullOrWhiteSpace(answer.Key) ? "a" + (answer.Id > 0 ? answer.Id.ToString() : "n" + index++) : answer.Key;
			}
		}

		#endregion Creation wizard and editor

		#region Review, approval and publish

		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> ReviewQueue()
		{
			var reader = await ReaderAsync();
			if (!reader.IsManager || _library == null)
				return Unauthorized();

			var model = new ProtocolReviewQueueModel { Items = await _library.GetReviewQueueAsync(reader), Names = await NamesAsync() };
			if (_roles != null)
				foreach (var role in await _roles.GetRolesForDepartmentAsync(DepartmentId) ?? new List<PersonnelRole>())
					model.Roles[role.PersonnelRoleId] = role.Name;
			return View(model);
		}

		/// <summary>The approver's review of one version: the redline against the version in force first, then comments and steps.</summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> Review(int id)
		{
			var reader = await ReaderAsync();
			if (!reader.IsManager || _library == null)
				return Unauthorized();

			var version = await _library.GetVersionAsync(DepartmentId, id);
			var protocol = version == null ? null : await _protocolsService.GetProtocolHeaderByIdAsync(version.DispatchProtocolId);
			if (protocol == null || protocol.DepartmentId != DepartmentId)
				return NotFound();

			var current = protocol.CurrentVersionId.HasValue && protocol.CurrentVersionId.Value != version.ProtocolVersionId
				? await _library.GetVersionAsync(DepartmentId, protocol.CurrentVersionId.Value) : null;
			var approvals = await _library.GetApprovalsAsync(DepartmentId, id);
			var round = approvals.Count == 0 ? new List<ProtocolVersionApproval>() : approvals.Where(a => a.Round == approvals.Max(x => x.Round)).OrderBy(a => a.StepOrder).ToList();
			var step = round.FirstOrDefault(a => a.Decision == (int)ProtocolApprovalDecisions.Pending);
			var roleIds = reader.Facts?.RoleIds?.ToList() ?? new List<int>();
			var comments = (await _library.GetCommentsAsync(DepartmentId, protocol.DispatchProtocolId)).Where(c => c.ProtocolVersionId == id && c.Kind is (int)ProtocolCommentKinds.ReviewComment or (int)ProtocolCommentKinds.Reply).ToList();
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);

			var model = new ProtocolReviewModel
			{
				Protocol = protocol,
				Version = version,
				Current = current,
				Diff = ProtocolVersionDiff.Compare(current?.ContentHtml, version.ContentHtml),
				Steps = round,
				Comments = comments,
				Outline = ProtocolContentDocument.OutlineFor(version.ContentJson, version.ContentHtml),
				Names = await NamesAsync(),
				OpenComments = comments.Count(c => c.Kind == (int)ProtocolCommentKinds.ReviewComment && !c.ParentCommentId.HasValue && !c.ResolvedOn.HasValue),
				CanDecide = version.Status == (int)ProtocolVersionStatuses.InReview && step != null && ProtocolAccessRules.CanDecide(reader, step, roleIds, round),
				CanPublish = version.Status == (int)ProtocolVersionStatuses.Approved && reader.MayApprove,
				CanWithdraw = (version.Status == (int)ProtocolVersionStatuses.InReview || version.Status == (int)ProtocolVersionStatuses.Approved) &&
					(reader.MayApprove || string.Equals(version.SubmittedByUserId, UserId, StringComparison.OrdinalIgnoreCase)),
				Message = TempData?["ProtocolMessage"] as string,
				Error = TempData?["ProtocolError"] as string,
				TimeZoneName = department?.TimeZone
			};

			if (_roles != null)
				foreach (var role in await _roles.GetRolesForDepartmentAsync(DepartmentId) ?? new List<PersonnelRole>())
					model.Roles[role.PersonnelRoleId] = role.Name;

			if (_signatures != null)
				model.Signatures = new SignatureListModel
				{
					Items = await _signatures.GetVerifiedForSubjectAsync(DepartmentId, SignatureSubjectKinds.ProtocolVersion, id.ToString(CultureInfo.InvariantCulture)),
					EmptyText = Signing("NoSignatures")
				};

			if (model.CanDecide)
			{
				var settings = await _library.GetSettingsAsync(DepartmentId);
				var later = round.Where(a => a.StepOrder > step.StepOrder && a.Decision == (int)ProtocolApprovalDecisions.Pending)
					.Select(a => !string.IsNullOrWhiteSpace(a.ApproverUserId) ? NameOf(model.Names, a.ApproverUserId)
						: a.ApproverRoleId.HasValue && model.Roles.TryGetValue(a.ApproverRoleId.Value, out var roleName) ? roleName : Signing("AnyApprover")).ToList();
				model.Panel = new SignaturePanelModel
				{
					Title = Text("ApproveHeader"),
					Statement = ProtocolSignatureStatements.Approval(protocol, version, step.StepOrder, round.Count, Signing("ProtocolApprovalStatement")),
					ArtifactSha256 = version.ContentHash,
					RequiredLevel = (int)SignaturePolicy.RequiredLevel(SignatureUse.ProtocolApproval, null),
					RequirePasskey = settings.RequirePasskeyForApproval,
					OthersToSign = later,
					SignAction = Url.Action("Approve", "Protocols", new { area = "User" }),
					PasskeyAction = _passkeys == null ? null : Url.Action("ApproveWithPasskey", "Protocols", new { area = "User" }),
					PasskeyOptionsUrl = _passkeys == null ? null : Url.Action("SignaturePasskeyOptions", "Protocols", new { area = "User" }),
					Fields = new Dictionary<string, string> { ["id"] = id.ToString(CultureInfo.InvariantCulture), ["purpose"] = "approve" },
					CommentField = "comment",
					SubmitLabel = Signing("SignApproveButton"),
					Disabled = model.OpenComments > 0,
					DisabledReason = model.OpenComments > 0 ? Text("LifecycleErrorOpenComments") : null
				};
			}

			return View(model);
		}

		private SignatureProof StepUpProof()
		{
			var proof = SignatureProof.SessionOnly(UserId, IpAddressHelper.GetRequestIP(Request, true), Request?.Headers["User-Agent"].ToString());
			// Set by RequiresRecentTwoFactor(RequireForOperation = true) from this session's server-side evidence.
			proof.StepUpVerifiedOnUtc = HttpContext?.Items["mfa_verified_at"] as DateTime?;
			return proof;
		}

		/// <summary>The artifact a signing passkey ceremony must be bound to, resolved on the server from the subject (never posted).</summary>
		private async Task<string> SigningHashAsync(string purpose, int id)
		{
			if (_library == null)
				return null;

			if (purpose == "approve")
				return (await _library.GetVersionAsync(DepartmentId, id))?.ContentHash;

			if (purpose == "review")
			{
				var protocol = await _protocolsService.GetProtocolHeaderByIdAsync(id);
				return protocol?.DepartmentId == DepartmentId && protocol.CurrentVersionId.HasValue
					? (await _library.GetVersionAsync(DepartmentId, protocol.CurrentVersionId.Value))?.ContentHash : null;
			}

			return null;
		}

		private async Task<PasskeyCaller> PasskeyCallerAsync()
		{
			var user = _userManager == null ? null : await _userManager.GetUserAsync(User);
			return user == null ? null : PasskeyCaller.From(HttpProtectedGrantContext.SessionOf(HttpContext), user.Id, user.UserName, DepartmentId,
				SystemAuditSystems.Website, IpAddressHelper.GetRequestIP(Request, true));
		}

		/// <summary>Passkey options whose WebAuthn challenge embeds the hash of the content being signed (S0, L2).</summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> SignaturePasskeyOptions(string purpose, int id, CancellationToken cancellationToken = default)
		{
			var hash = await SigningHashAsync(purpose, id);
			var caller = await PasskeyCallerAsync();
			if (_passkeys == null || hash == null || caller == null)
				return Json(new { success = false, error = "invalid_request" });

			var start = await _passkeys.BeginSigningAssertionAsync(caller, hash, cancellationToken);
			return start.Succeeded
				? Json(new { success = true, requestId = start.RequestId, options = start.OptionsJson })
				: Json(new { success = false, error = PasskeyOutcomes.ErrorCode(start.Outcome) });
		}

		private async Task<SignatureProof> PasskeyProofAsync(string purpose, int id, string requestId, string credential, CancellationToken cancellationToken)
		{
			var hash = await SigningHashAsync(purpose, id);
			var caller = await PasskeyCallerAsync();
			var proof = SignatureProof.SessionOnly(UserId, IpAddressHelper.GetRequestIP(Request, true), Request?.Headers["User-Agent"].ToString());
			if (_passkeys == null || hash == null || caller == null || string.IsNullOrWhiteSpace(requestId) || string.IsNullOrWhiteSpace(credential))
				return proof;

			var assertion = await _passkeys.CompleteSigningAssertionAsync(caller, requestId, credential, hash, cancellationToken);
			if (assertion.Succeeded)
			{
				proof.PasskeyId = assertion.Passkey.UserPasskeyId;
				proof.PasskeyChallengeId = requestId;
				proof.PasskeyBoundSha256 = hash;
				proof.PasskeyVerifiedOnUtc = assertion.VerifiedOnUtc;
			}

			return proof;
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_Approve)]
		[Resgrid.Web.Attributes.RequiresRecentTwoFactor(RequireForOperation = true)]
		public async Task<IActionResult> Approve(int id, string comment, CancellationToken cancellationToken = default) =>
			await ApproveCoreAsync(id, comment, StepUpProof(), cancellationToken);

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_Approve)]
		public async Task<IActionResult> ApproveWithPasskey(int id, string comment, string passkeyRequestId, string passkeyCredential, CancellationToken cancellationToken = default) =>
			await ApproveCoreAsync(id, comment, await PasskeyProofAsync("approve", id, passkeyRequestId, passkeyCredential, cancellationToken), cancellationToken);

		private async Task<IActionResult> ApproveCoreAsync(int id, string comment, SignatureProof proof, CancellationToken cancellationToken)
		{
			if (_library == null)
				return NotFound();

			var version = await _library.GetVersionAsync(DepartmentId, id);
			var protocol = version == null ? null : await _protocolsService.GetProtocolHeaderByIdAsync(version.DispatchProtocolId);
			var approvals = version == null ? new List<ProtocolVersionApproval>() : await _library.GetApprovalsAsync(DepartmentId, id);
			var round = approvals.Count == 0 ? new List<ProtocolVersionApproval>() : approvals.Where(a => a.Round == approvals.Max(x => x.Round)).OrderBy(a => a.StepOrder).ToList();
			var step = round.FirstOrDefault(a => a.Decision == (int)ProtocolApprovalDecisions.Pending);
			var statement = protocol == null || step == null ? null : ProtocolSignatureStatements.Approval(protocol, version, step.StepOrder, round.Count, Signing("ProtocolApprovalStatement"));

			var outcome = await _library.ApproveAsync(await ReaderAsync(), id, comment, proof, statement, cancellationToken);
			TempData[outcome.Succeeded ? "ProtocolMessage" : "ProtocolError"] = outcome.Succeeded
				? Text(outcome.Version?.Status == (int)ProtocolVersionStatuses.Approved ? "ApprovedAllMessage" : "ApprovedStepMessage")
				: LifecycleError(outcome.Error);
			return RedirectToAction("Review", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_Approve)]
		public async Task<IActionResult> Reject(int id, string comment, CancellationToken cancellationToken = default)
		{
			if (_library == null)
				return NotFound();

			var outcome = await _library.RejectAsync(await ReaderAsync(), id, comment, cancellationToken);
			TempData[outcome.Succeeded ? "ProtocolMessage" : "ProtocolError"] = outcome.Succeeded ? Text("RejectedMessage") : LifecycleError(outcome.Error);
			return outcome.Succeeded && outcome.Protocol != null ? RedirectToAction("View", new { id = outcome.Protocol.DispatchProtocolId }) : RedirectToAction("Review", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_Approve)]
		public async Task<IActionResult> Publish(int id, bool major, string changeSummary, string effectiveOn, string expiresOn, CancellationToken cancellationToken = default)
		{
			if (_library == null)
				return NotFound();

			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			var time = new DepartmentTime(department);
			var effective = string.IsNullOrWhiteSpace(effectiveOn) ? (DateTime?)null : time.Parse(effectiveOn);
			var expires = string.IsNullOrWhiteSpace(expiresOn) ? (DateTime?)null : time.Parse(expiresOn);

			var outcome = await _library.PublishAsync(await ReaderAsync(), id, major, changeSummary, effective, expires, cancellationToken);
			if (!outcome.Succeeded)
			{
				TempData["ProtocolError"] = LifecycleError(outcome.Error);
				return RedirectToAction("Review", new { id });
			}

			TempData["ProtocolMessage"] = string.Format(Text(outcome.Version.Status == (int)ProtocolVersionStatuses.Published ? "PublishedMessage" : "ScheduledMessage"),
				outcome.Version.Label, time.Format(outcome.Version.EffectiveOn));
			return RedirectToAction("View", new { id = outcome.Protocol.DispatchProtocolId });
		}

		private async Task<SignaturePanelModel> ReviewPanelAsync(DispatchProtocol protocol, ProtocolVersion current)
		{
			var settings = _library == null ? new ProtocolLibrarySettings() : await _library.GetSettingsAsync(DepartmentId);
			return new SignaturePanelModel
			{
				Title = Text("RecordReviewHeader"),
				Statement = ProtocolSignatureStatements.Review(protocol, current, Signing("ProtocolReviewStatement")),
				ArtifactSha256 = current.ContentHash,
				RequiredLevel = (int)SignaturePolicy.RequiredLevel(SignatureUse.ProtocolReviewStamp, null),
				RequirePasskey = settings.RequirePasskeyForApproval,
				SignAction = Url.Action("RecordReview", "Protocols", new { area = "User" }),
				PasskeyAction = _passkeys == null ? null : Url.Action("RecordReviewWithPasskey", "Protocols", new { area = "User" }),
				PasskeyOptionsUrl = _passkeys == null ? null : Url.Action("SignaturePasskeyOptions", "Protocols", new { area = "User" }),
				Fields = new Dictionary<string, string> { ["id"] = protocol.DispatchProtocolId.ToString(CultureInfo.InvariantCulture), ["purpose"] = "review" },
				SubmitLabel = Signing("SignReviewButton"),
				Note = Text("RecordReviewHelp")
			};
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		[Resgrid.Web.Attributes.RequiresRecentTwoFactor(RequireForOperation = true)]
		public async Task<IActionResult> RecordReview(int id, CancellationToken cancellationToken = default) =>
			await RecordReviewCoreAsync(id, StepUpProof(), cancellationToken);

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> RecordReviewWithPasskey(int id, string passkeyRequestId, string passkeyCredential, CancellationToken cancellationToken = default) =>
			await RecordReviewCoreAsync(id, await PasskeyProofAsync("review", id, passkeyRequestId, passkeyCredential, cancellationToken), cancellationToken);

		private async Task<IActionResult> RecordReviewCoreAsync(int id, SignatureProof proof, CancellationToken cancellationToken)
		{
			if (_library == null)
				return NotFound();

			var protocol = await _protocolsService.GetProtocolHeaderByIdAsync(id);
			var current = protocol?.CurrentVersionId == null ? null : await _library.GetVersionAsync(DepartmentId, protocol.CurrentVersionId.Value);
			var statement = protocol == null || current == null ? null : ProtocolSignatureStatements.Review(protocol, current, Signing("ProtocolReviewStatement"));

			var outcome = await _library.RecordReviewAsync(await ReaderAsync(), id, proof, statement, cancellationToken);
			TempData[outcome.Succeeded ? "ProtocolMessage" : "ProtocolError"] = outcome.Succeeded ? Text("ReviewRecordedMessage") : LifecycleError(outcome.Error);
			return RedirectToAction("View", new { id });
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_Approve)]
		public async Task<IActionResult> Retire(int id, CancellationToken cancellationToken = default)
		{
			if (_library == null)
				return NotFound();

			var outcome = await _library.RetireAsync(await ReaderAsync(), id, cancellationToken);
			TempData[outcome.Succeeded ? "ProtocolMessage" : "ProtocolError"] = outcome.Succeeded ? Text("RetiredMessage") : LifecycleError(outcome.Error);
			return RedirectToAction("View", new { id });
		}

		/// <summary>Version compare: a word-level redline between two versions of the protocol the reader may read.</summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> Compare(int id, int? from = null, int? to = null)
		{
			var protocol = await _protocolsService.GetProtocolHeaderByIdAsync(id);
			if (protocol == null || protocol.DepartmentId != DepartmentId || _library == null || _access == null)
				return NotFound();

			var reader = await ReaderAsync();
			if (!await _access.CanReadAsync(reader, protocol))
				return NotFound();

			var audience = await _library.GetAudienceAsync(DepartmentId, id);
			var versions = (await _library.GetVersionsAsync(DepartmentId, id)).Where(v => ProtocolAccessRules.CanReadVersion(reader, protocol, audience, v)).ToList();
			var toHeader = to.HasValue ? versions.FirstOrDefault(v => v.ProtocolVersionId == to.Value) : versions.FirstOrDefault(v => v.ProtocolVersionId == protocol.CurrentVersionId) ?? versions.FirstOrDefault();
			if (toHeader == null)
				return NotFound();
			var fromHeader = from.HasValue ? versions.FirstOrDefault(v => v.ProtocolVersionId == from.Value)
				: versions.Where(v => v.ProtocolVersionId < toHeader.ProtocolVersionId && v.Status != (int)ProtocolVersionStatuses.Legacy).OrderByDescending(v => v.ProtocolVersionId).FirstOrDefault();
			if (from.HasValue && fromHeader == null)
				return NotFound();

			var toVersion = await _library.GetVersionAsync(DepartmentId, toHeader.ProtocolVersionId);
			var fromVersion = fromHeader == null ? null : await _library.GetVersionAsync(DepartmentId, fromHeader.ProtocolVersionId);

			return View(new ProtocolCompareModel
			{
				Protocol = protocol,
				From = fromVersion,
				To = toVersion,
				Versions = versions,
				Diff = ProtocolVersionDiff.Compare(fromVersion?.ContentHtml, toVersion.ContentHtml)
			});
		}

		#endregion Review, approval and publish

		#region Categories, settings, import and export

		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_Update)]
		public async Task<IActionResult> Categories()
		{
			if (_library == null)
				return NotFound();

			var protocols = await _protocolsService.GetProtocolSummariesForDepartmentAsync(DepartmentId);
			var model = new ProtocolCategoriesModel
			{
				Categories = await _library.GetCategoriesAsync(DepartmentId),
				Counts = protocols.Where(p => p.CategoryId.HasValue).GroupBy(p => p.CategoryId.Value).ToDictionary(g => g.Key, g => g.Count()),
				Message = TempData?["ProtocolMessage"] as string,
				Error = TempData?["ProtocolError"] as string
			};
			var lists = await EditorListsAsync();
			model.Roles = lists.Roles;
			model.Members = lists.Members;
			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_Approve)]
		public async Task<IActionResult> SaveCategory(ProtocolCategoryInput input, CancellationToken cancellationToken = default)
		{
			if (_library == null || input == null)
				return NotFound();

			if (string.IsNullOrWhiteSpace(input.Name))
			{
				TempData["ProtocolError"] = Text("ErrorCategoryName");
				return RedirectToAction("Categories");
			}

			var steps = new List<ProtocolApprovalStep>();
			foreach (var value in input.Approvers ?? new List<string>())
			{
				if (string.IsNullOrWhiteSpace(value))
					continue;
				if (value.StartsWith("u:", StringComparison.Ordinal))
				{
					var member = await _departmentsService.GetDepartmentMemberAsync(value.Substring(2), DepartmentId, false);
					if (member != null && member.DepartmentId == DepartmentId)
						steps.Add(new ProtocolApprovalStep { ApproverUserId = member.UserId });
				}
				else if (value.StartsWith("r:", StringComparison.Ordinal) && int.TryParse(value.Substring(2), NumberStyles.None, CultureInfo.InvariantCulture, out var roleId))
				{
					steps.Add(new ProtocolApprovalStep { ApproverRoleId = roleId });
				}
			}

			var saved = await _library.SaveCategoryAsync(DepartmentId, new ProtocolCategory
			{
				ProtocolCategoryId = input.ProtocolCategoryId,
				Name = input.Name,
				ParentCategoryId = input.ParentCategoryId,
				Kind = input.Kind,
				SortOrder = input.SortOrder,
				ApprovalSteps = steps
			}, UserId, cancellationToken);

			TempData[saved == null ? "ProtocolError" : "ProtocolMessage"] = Text(saved == null ? "ErrorCategoryName" : "CategorySavedMessage");
			return RedirectToAction("Categories");
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_Approve)]
		public async Task<IActionResult> DeleteCategory(int id, CancellationToken cancellationToken = default)
		{
			if (_library == null)
				return NotFound();

			var deleted = await _library.DeleteCategoryAsync(DepartmentId, id, UserId, cancellationToken);
			TempData[deleted ? "ProtocolMessage" : "ProtocolError"] = Text(deleted ? "CategoryDeletedMessage" : "CategoryInUseMessage");
			return RedirectToAction("Categories");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> Settings()
		{
			if (_library == null || !(await ReaderAsync()).IsDepartmentAdmin)
				return Unauthorized();

			var settings = await _library.GetSettingsAsync(DepartmentId);
			var model = new ProtocolSettingsModel
			{
				RequirePasskeyForApproval = settings.RequirePasskeyForApproval,
				ReadAndSignRequiresStepUp = settings.ReadAndSignRequiresStepUp,
				DefaultReviewIntervalDays = settings.DefaultReviewIntervalDays,
				LegalHold = settings.LegalHold,
				DefaultContentLocale = settings.DefaultContentLocale,
				ShowDeterminantInDispatch = settings.ShowDeterminantInDispatch,
				RetentionDays = Enum.GetValues(typeof(ProtocolKinds)).Cast<int>().Select(k => settings.KeepDaysFor(k) ?? 0).ToList(),
				Message = TempData?["ProtocolMessage"] as string
			};
			return View(model);
		}

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		[Resgrid.Web.Attributes.RequiresRecentTwoFactor]
		public async Task<IActionResult> Settings(ProtocolSettingsModel model, CancellationToken cancellationToken = default)
		{
			if (_library == null || !(await ReaderAsync()).IsDepartmentAdmin)
				return Unauthorized();

			model ??= new ProtocolSettingsModel();
			var kinds = Enum.GetValues(typeof(ProtocolKinds)).Cast<int>().ToList();
			await _library.SaveSettingsAsync(DepartmentId, new ProtocolLibrarySettings
			{
				RequirePasskeyForApproval = model.RequirePasskeyForApproval,
				ReadAndSignRequiresStepUp = model.ReadAndSignRequiresStepUp,
				DefaultReviewIntervalDays = model.DefaultReviewIntervalDays,
				LegalHold = model.LegalHold,
				DefaultContentLocale = model.DefaultContentLocale,
				ShowDeterminantInDispatch = model.ShowDeterminantInDispatch,
				Retention = kinds.Select((k, i) => new ProtocolKindRetention { Kind = k, KeepDays = model.RetentionDays != null && i < model.RetentionDays.Count ? Math.Max(0, model.RetentionDays[i]) : 0 })
					.Where(r => r.KeepDays > 0).ToList()
			}, UserId, cancellationToken);

			TempData["ProtocolMessage"] = Text("SettingsSavedMessage");
			return RedirectToAction("Settings");
		}

		/// <summary>The library in the open exchange format (JSON): every protocol, or the ones asked for.</summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_Update)]
		public async Task<IActionResult> Export(string ids = null)
		{
			if (_library == null)
				return NotFound();

			var wanted = (ids ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries)
				.Select(x => int.TryParse(x, NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : 0).Where(v => v > 0).ToList();
			var document = await _library.ExportAsync(await ReaderAsync(), wanted);
			var json = JsonConvert.SerializeObject(document, Formatting.Indented);
			return File(Encoding.UTF8.GetBytes(json), "application/json", $"protocols-{DateTime.UtcNow:yyyyMMdd}.json");
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_Create)]
		public IActionResult Import() => View(new ProtocolImportModel());

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_Create)]
		public async Task<IActionResult> Import(IFormFile file, CancellationToken cancellationToken = default)
		{
			if (_library == null)
				return NotFound();

			var result = new ProtocolImportResult();
			if (file == null || file.Length == 0 || file.Length > 10_000_000)
			{
				result.Error = "file";
			}
			else
			{
				try
				{
					using var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8);
					var document = JsonConvert.DeserializeObject<ProtocolExchangeDocument>(await reader.ReadToEndAsync(cancellationToken));
					result = await _library.ImportAsync(await ReaderAsync(), document, cancellationToken);
				}
				catch (JsonException)
				{
					result.Error = "unknown_format";
				}
			}

			return View(new ProtocolImportModel { Result = result });
		}

		#endregion Categories, settings, import and export

		#region Disable and delete

		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_Update)]
		public async Task<IActionResult> SetDisabled(int id, bool disabled, string returnTo = null, CancellationToken cancellationToken = default)
		{
			if (!await _authorizationService.CanUserModifyProtocolAsync(UserId, id))
				return Unauthorized();

			var before = await _protocolsService.GetProtocolHeaderByIdAsync(id);
			var beforeSnapshot = Snapshot(before);
			var wasDisabled = before?.IsDisabled ?? false;

			var saved = await _protocolsService.SetProtocolDisabledAsync(id, disabled, UserId, cancellationToken);
			if (saved == null)
				return NotFound();

			if (wasDisabled != saved.IsDisabled)
				Audit(saved.IsDisabled ? AuditLogTypes.ProtocolDisabled : AuditLogTypes.ProtocolEnabled, beforeSnapshot, saved);

			TempData["ProtocolMessage"] = Text(saved.IsDisabled ? "ProtocolDisabledMessage" : "ProtocolEnabledMessage");

			return returnTo == "view" ? RedirectToAction("View", new { id }) : RedirectToAction("Index");
		}

		/// <summary>
		/// A soft delete (the row and every version stay for the calls and history that cite them; R-P10). Approvers only
		/// (Protocols:Delete comes with Approve Protocols).
		/// </summary>
		[HttpPost]
		[ValidateAntiForgeryToken]
		[Authorize(Policy = ResgridResources.Protocol_Delete)]
		public async Task<IActionResult> Delete(int id)
		{
			var protocol = await _protocolsService.GetProtocolHeaderByIdAsync(id);
			if (protocol == null || protocol.DepartmentId != DepartmentId || protocol.IsDeleted)
				return Unauthorized();

			if (await _protocolsService.DeleteProtocol(id))
				Audit(AuditLogTypes.ProtocolDeleted, Snapshot(protocol), null);

			return RedirectToAction("Index");
		}

		#endregion Disable and delete

		#region JSON for the call forms

		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> GetProtocol(int id)
		{
			if (!await _authorizationService.CanUserViewProtocolAsync(UserId, id))
				return Unauthorized();

			var protocol = await _protocolsService.GetProtocolByIdAsync(id);
			if (protocol == null)
				return NotFound();

			// A DTO, never the entity: the entity carried attachment bytes (D20).
			var json = new ProtocolDetailJson
			{
				IsDisabled = protocol.IsDisabled,
				ProtocolText = StringHelpers.SanitizeHtmlForDisplay(protocol.ProtocolText),
				CreatedOn = protocol.CreatedOn,
				UpdatedOn = protocol.UpdatedOn
			};
			Fill(json, protocol, null, null, null);

			return Json(json);
		}

		/// <summary>
		/// The department's enabled, published Dispatch and Procedure protocols evaluated for a call being written: the shared
		/// matcher (the v4 Protocols/GetProtocolsForCall runs the same one), with the call type by id or name, and trigger windows
		/// compared with the call's logged time (department local, optional; now when blank).
		/// </summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> GetProtocolsForCall(int priority, string type = null, int? callTypeId = null, string loggedOn = null, int? callId = null,
			string nature = null, double? latitude = null, double? longitude = null, int? locationPoiId = null)
		{
			var department = await _departmentsService.GetDepartmentByIdAsync(DepartmentId, false);
			var at = string.IsNullOrWhiteSpace(loggedOn) ? (DateTime?)null : new DepartmentTime(department).Parse(loggedOn);
			var onCall = new HashSet<int>();
			// P4 trigger facts from the form being written (keywords, zone, POI type); a saved call's own facts win below.
			var details = new DispatchTriggerCallInput { Nature = nature, Latitude = latitude, Longitude = longitude, LocationPoiId = locationPoiId };

			// Update Call: matched at the call's own logged time, and the protocols already on it (removed ones too) left out.
			if (callId.HasValue && callId.Value > 0 && await _authorizationService.CanUserViewCallAsync(UserId, callId.Value))
			{
				var call = await _callsService.GetCallByIdAsync(callId.Value);
				if (call != null && call.DepartmentId == DepartmentId)
				{
					at = call.LoggedOn;
					onCall = new HashSet<int>((await _callsService.GetCallProtocolsByCallIdAsync(call.CallId) ?? new List<CallProtocol>()).Select(x => x.DispatchProtocolId));
					var stored = DispatchTriggerCallInput.FromCall(call);
					details = new DispatchTriggerCallInput
					{
						Nature = string.IsNullOrWhiteSpace(nature) ? stored.Nature : nature,
						Notes = stored.Notes,
						GeoLocationData = latitude.HasValue && longitude.HasValue ? null : stored.GeoLocationData,
						Latitude = latitude,
						Longitude = longitude,
						LocationPoiId = locationPoiId ?? stored.LocationPoiId,
						CallSource = stored.CallSource
					};
				}
			}

			details.LoggedOnUtc = at;
			var context = await _protocolsService.BuildCallContextWithFactsAsync(DepartmentId, priority, callTypeId, type, details);
			var matches = (await _protocolsService.GetProtocolsForCallAsync(DepartmentId, context)).Where(x => !onCall.Contains(x.Protocol.DispatchProtocolId)).ToList();
			var priorities = await _callsService.GetCallPrioritiesForDepartmentAsync(DepartmentId) ?? new List<DepartmentCallPriority>();
			var types = await _callsService.GetCallTypesForDepartmentAsync(DepartmentId) ?? new List<CallType>();

			return Json(matches.Select(m =>
			{
				var json = new ProtocolJson();
				Fill(json, m.Protocol, m, priorities, types);

				// Matching protocols only carry their questions (D20); the rest are names for the "add by hand" picker.
				if (m.State == ProtocolStates.Inactive)
					json.Questions = new List<ProtocolQuestionJson>();

				return json;
			}).ToList());
		}

		/// <summary>Kept for pages cached before P0; the same as <see cref="GetProtocolsForCall"/>.</summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public Task<IActionResult> GetProtocolsForPrioType(int priority, string type)
		{
			return GetProtocolsForCall(priority, type);
		}

		private void Fill(ProtocolJson json, DispatchProtocol protocol, ProtocolMatch match, List<DepartmentCallPriority> priorities, List<CallType> types)
		{
			json.Id = protocol.DispatchProtocolId;
			json.Name = protocol.Name;
			json.Code = protocol.Code;
			json.Description = StringHelpers.SanitizeHtmlForDisplay(protocol.Description);
			json.MinimumWeight = protocol.MinimumWeight;
			json.State = (int)(match?.State ?? ProtocolStates.Inactive);
			json.MatchedTriggerType = match?.MatchedTrigger?.Type;
			json.HasQuestions = protocol.Questions != null && protocol.Questions.Count > 0;
			json.Reason = match?.MatchedTrigger == null ? string.Empty : ProtocolDisplay.Reason(match.MatchedTrigger, priorities, types, _localizer);
			json.Questions = (protocol.Questions ?? Enumerable.Empty<DispatchProtocolQuestion>()).OrderBy(q => q.DispatchProtocolQuestionId).Select(q => new ProtocolQuestionJson
			{
				Id = q.DispatchProtocolQuestionId,
				Question = q.Question,
				Answers = (q.Answers ?? Enumerable.Empty<DispatchProtocolQuestionAnswer>()).OrderBy(a => a.DispatchProtocolQuestionAnswerId)
					.Select(a => new ProtocolAnswerJson { Id = a.DispatchProtocolQuestionAnswerId, Answer = a.Answer, Weight = a.Weight }).ToList()
			}).ToList();
			json.Attachments = (protocol.Attachments ?? Enumerable.Empty<DispatchProtocolAttachment>())
				.Select(a => new ProtocolAttachmentJson { Id = a.DispatchProtocolAttachmentId, FileName = a.FileName, FileType = a.FileType, Size = a.Size }).ToList();
		}

		/// <summary>The one web endpoint attachment bytes leave through: the protocol must be the caller's department's and readable to them.</summary>
		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> GetProtocolAttachment(int protocolAttachmentId)
		{
			var attachment = await _protocolsService.GetAttachmentForDepartmentAsync(DepartmentId, protocolAttachmentId);

			if (attachment == null || attachment.Data == null)
				return NotFound();

			if (!await _authorizationService.CanUserViewProtocolAsync(UserId, attachment.DispatchProtocolId))
				return NotFound();

			return new FileContentResult(attachment.Data, string.IsNullOrWhiteSpace(attachment.FileType) ? "application/octet-stream" : attachment.FileType)
			{
				FileDownloadName = attachment.FileName
			};
		}

		[HttpGet]
		[Authorize(Policy = ResgridResources.Protocol_View)]
		public async Task<IActionResult> GetTextForProtocol(int id)
		{
			if (!await _authorizationService.CanUserViewProtocolAsync(UserId, id))
				return Unauthorized();

			var protocol = await _protocolsService.GetProtocolHeaderByIdAsync(id);
			if (protocol == null)
				return NotFound();

			var protocolText = Text("NoProtocolText");

			if (!String.IsNullOrWhiteSpace(protocol.ProtocolText))
				protocolText = StringHelpers.SanitizeHtmlForDisplay(protocol.ProtocolText);

			dynamic protocolJson = new
			{
				Name = protocol.Name,
				Text = protocolText
			};

			return Json(protocolJson);
		}

		#endregion JSON for the call forms

		#region Audit

		private static string Snapshot(DispatchProtocol protocol)
		{
			if (protocol == null)
				return null;

			return JsonConvert.SerializeObject(new
			{
				protocol.DispatchProtocolId,
				protocol.Name,
				protocol.Code,
				protocol.Kind,
				protocol.Status,
				protocol.IsDisabled,
				protocol.IsDeleted,
				protocol.MinimumWeight,
				Triggers = protocol.Triggers?.Count,
				Questions = protocol.Questions?.Count,
				Attachments = protocol.Attachments?.Count
			});
		}

		private void Audit(AuditLogTypes type, string before, DispatchProtocol after)
		{
			if (_eventAggregator == null)
				return;

			var auditEvent = new AuditEvent
			{
				DepartmentId = DepartmentId,
				UserId = UserId,
				Type = type,
				Before = before,
				After = Snapshot(after),
				Successful = true,
				IpAddress = IpAddressHelper.GetRequestIP(Request, true),
				ServerName = Environment.MachineName,
				UserAgent = $"{Request?.Headers["User-Agent"]} {Request?.Headers["Accept-Language"]}"
			};

			_eventAggregator.SendMessage<AuditEvent>(auditEvent);
		}

		#endregion Audit
	}
}
