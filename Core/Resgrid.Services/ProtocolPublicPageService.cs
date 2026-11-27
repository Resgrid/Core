using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Opt-in public pages (Protocols P5, decision 6). A link is made by an approver or a department admin for a published protocol (its
	/// version in force, or one pinned version) or a category as a set, and revoked at once. Every request re-checks every protocol against
	/// <see cref="ProtocolPublicPages.Eligibility"/>, so a protocol that later becomes audience-restricted, licensed, document-backed, disabled
	/// or unpublished drops off its page without anyone touching the link; the page carries only the <see cref="PublicProtocolEntry"/> fields.
	/// </summary>
	public class ProtocolPublicPageService : IProtocolPublicPageService
	{
		public const int MaxLinksPerDepartment = 500;

		private readonly IProtocolPublicLinksRepository _links;
		private readonly IProtocolsService _protocolsService;
		private readonly IProtocolLibraryService _library;
		private readonly IProtocolVersionsRepository _versions;
		private readonly IContentAttributionsRepository _attributions;
		private readonly IProtocolTranslationService _translations;
		private readonly IDepartmentsService _departments;
		private readonly IEventAggregator _eventAggregator;

		public ProtocolPublicPageService(IProtocolPublicLinksRepository links, IProtocolsService protocolsService, IProtocolLibraryService library,
			IProtocolVersionsRepository versions, IContentAttributionsRepository attributions, IProtocolTranslationService translations, IDepartmentsService departments,
			IEventAggregator eventAggregator)
		{
			_links = links;
			_protocolsService = protocolsService;
			_library = library;
			_versions = versions;
			_attributions = attributions;
			_translations = translations;
			_departments = departments;
			_eventAggregator = eventAggregator;
		}

		#region Managing links

		public async Task<List<ProtocolPublicLinkView>> GetLinksAsync(ProtocolReader reader, bool includeRevoked)
		{
			var views = new List<ProtocolPublicLinkView>();
			if (reader == null || !reader.MayApprove)
				return views;

			var links = await _links.GetForDepartmentAsync(reader.DepartmentId, includeRevoked) ?? new List<ProtocolPublicLink>();
			if (links.Count == 0)
				return views;

			var protocols = (await _protocolsService.GetProtocolSummariesForDepartmentAsync(reader.DepartmentId) ?? new List<DispatchProtocol>())
				.Where(p => p.DepartmentId == reader.DepartmentId).ToDictionary(p => p.DispatchProtocolId);
			var categories = await _library.GetCategoriesAsync(reader.DepartmentId) ?? new List<ProtocolCategory>();
			var audiences = (await _library.GetDepartmentAudiencesAsync(reader.DepartmentId) ?? new List<ProtocolAudience>()).ToLookup(a => a.DispatchProtocolId);
			var headers = (await _library.GetDepartmentVersionHeadersAsync(reader.DepartmentId) ?? new List<ProtocolVersion>()).ToDictionary(v => v.ProtocolVersionId);
			var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (var person in await _departments.GetAllPersonnelNamesForDepartmentAsync(reader.DepartmentId) ?? new List<PersonName>())
				if (person != null && !string.IsNullOrWhiteSpace(person.UserId))
					names[person.UserId] = person.Name?.Trim();

			ProtocolPublicEligibility EligibilityOf(DispatchProtocol p) =>
				p == null ? ProtocolPublicEligibility.NotPublished
				: ProtocolPublicPages.Eligibility(p, p.CurrentVersionId.HasValue && headers.TryGetValue(p.CurrentVersionId.Value, out var v) ? v : null, audiences[p.DispatchProtocolId]);

			foreach (var link in links)
			{
				var view = new ProtocolPublicLinkView
				{
					Link = link,
					CreatedByName = !string.IsNullOrWhiteSpace(link.CreatedByUserId) && names.TryGetValue(link.CreatedByUserId, out var name) ? name : null
				};
				if (link.Kind == (int)ProtocolPublicLinkKinds.Category)
				{
					var category = categories.FirstOrDefault(c => c.ProtocolCategoryId == link.ProtocolCategoryId);
					view.Title = category?.Name;
					var within = category == null ? new HashSet<int>() : Descendants(categories, category.ProtocolCategoryId);
					view.ShownCount = protocols.Values.Count(p => p.CategoryId.HasValue && within.Contains(p.CategoryId.Value) && EligibilityOf(p) == ProtocolPublicEligibility.Eligible);
					view.Eligibility = category == null ? ProtocolPublicEligibility.NotPublished : ProtocolPublicEligibility.Eligible;
				}
				else
				{
					protocols.TryGetValue(link.DispatchProtocolId ?? 0, out var protocol);
					view.Title = protocol?.Name;
					view.Eligibility = EligibilityOf(protocol);
					var shownVersion = link.ProtocolVersionId ?? protocol?.CurrentVersionId;
					view.VersionLabel = shownVersion.HasValue && headers.TryGetValue(shownVersion.Value, out var version) ? version.Label : null;
					if (link.ProtocolVersionId.HasValue && protocol != null && protocol.CurrentVersionId != link.ProtocolVersionId)
						view.Eligibility = ProtocolPublicEligibility.NotPublished;
					view.ShownCount = view.Eligibility == ProtocolPublicEligibility.Eligible ? 1 : 0;
				}
				views.Add(view);
			}

			return views;
		}

		public async Task<ProtocolPublicEligibility> GetEligibilityAsync(int departmentId, int protocolId)
		{
			var (protocol, version, audience) = await LoadAsync(departmentId, protocolId, false);
			return ProtocolPublicPages.Eligibility(protocol, version, audience);
		}

		public async Task<ProtocolPublicLinkResult> CreateProtocolLinkAsync(ProtocolReader reader, int protocolId, bool pinVersion, bool embedEnabled, string embedOrigins,
			CancellationToken cancellationToken = default)
		{
			if (reader == null || !reader.MayApprove)
				return ProtocolPublicLinkResult.Fail(ProtocolPublicLinkErrors.NotAuthorized);

			var (protocol, version, audience) = await LoadAsync(reader.DepartmentId, protocolId, false);
			if (protocol == null)
				return ProtocolPublicLinkResult.Fail(ProtocolPublicLinkErrors.NotFound);

			var eligibility = ProtocolPublicPages.Eligibility(protocol, version, audience);
			if (eligibility != ProtocolPublicEligibility.Eligible)
				return ProtocolPublicLinkResult.Fail(ProtocolPublicLinkErrors.NotEligible, eligibility);

			var origins = ProtocolPublicPages.NormalizeEmbedOrigins(embedOrigins, out var valid);
			if (!valid)
				return ProtocolPublicLinkResult.Fail(ProtocolPublicLinkErrors.InvalidOrigins);

			var live = await _links.GetForDepartmentAsync(reader.DepartmentId, false) ?? new List<ProtocolPublicLink>();
			if (live.Count >= MaxLinksPerDepartment)
				return ProtocolPublicLinkResult.Fail(ProtocolPublicLinkErrors.TooManyLinks);
			int? pinned = pinVersion ? version.ProtocolVersionId : (int?)null;
			if (live.Any(l => l.Kind == (int)ProtocolPublicLinkKinds.Protocol && l.DispatchProtocolId == protocolId && l.ProtocolVersionId == pinned))
				return ProtocolPublicLinkResult.Fail(ProtocolPublicLinkErrors.AlreadyPublic);

			var link = await InsertAsync(new ProtocolPublicLink
			{
				DepartmentId = reader.DepartmentId,
				Kind = (int)ProtocolPublicLinkKinds.Protocol,
				DispatchProtocolId = protocolId,
				ProtocolVersionId = pinned,
				EmbedEnabled = embedEnabled,
				EmbedOrigins = string.IsNullOrEmpty(origins) ? null : origins,
				CreatedOn = DateTime.UtcNow,
				CreatedByUserId = reader.UserId
			}, cancellationToken);

			Audit(reader, AuditLogTypes.ProtocolPublicLinkCreated, null, link, version);
			RaiseLinkChanged(reader, link, protocol, version, null, "Created");
			return new ProtocolPublicLinkResult { Link = link };
		}

		/// <summary>Workflow trigger 250 after a public link was stored or revoked: the link's id, kind and target, never its token.</summary>
		private void RaiseLinkChanged(ProtocolReader reader, ProtocolPublicLink link, DispatchProtocol protocol, ProtocolVersion version, ProtocolCategory category, string action)
		{
			if (reader == null || link == null)
				return;

			var evt = ProtocolWorkflowEvent.For(protocol, version, ProtocolWorkflowChanges.PublicLinkChanged, reader.UserId, DateTime.UtcNow);
			evt.DepartmentId = reader.DepartmentId;
			evt.ProtocolId = link.DispatchProtocolId ?? 0;
			evt.CategoryId = link.ProtocolCategoryId ?? protocol?.CategoryId;
			if (protocol == null && category != null)
				evt.ProtocolName = category.Name;
			evt.PublicLinkId = link.ProtocolPublicLinkId;
			evt.PublicLinkKind = Enum.IsDefined(typeof(ProtocolPublicLinkKinds), link.Kind) ? ((ProtocolPublicLinkKinds)link.Kind).ToString() : null;
			evt.PublicLinkAction = action;
			evt.EmbedEnabled = link.EmbedEnabled;
			ContentWorkflowContext.Publish(_eventAggregator, evt);
		}

		public async Task<ProtocolPublicLinkResult> CreateCategoryLinkAsync(ProtocolReader reader, int categoryId, bool embedEnabled, string embedOrigins,
			CancellationToken cancellationToken = default)
		{
			if (reader == null || !reader.MayApprove)
				return ProtocolPublicLinkResult.Fail(ProtocolPublicLinkErrors.NotAuthorized);

			var categories = await _library.GetCategoriesAsync(reader.DepartmentId) ?? new List<ProtocolCategory>();
			var category = categories.FirstOrDefault(c => c.ProtocolCategoryId == categoryId && c.DepartmentId == reader.DepartmentId);
			if (category == null)
				return ProtocolPublicLinkResult.Fail(ProtocolPublicLinkErrors.NotFound);

			var origins = ProtocolPublicPages.NormalizeEmbedOrigins(embedOrigins, out var valid);
			if (!valid)
				return ProtocolPublicLinkResult.Fail(ProtocolPublicLinkErrors.InvalidOrigins);

			var live = await _links.GetForDepartmentAsync(reader.DepartmentId, false) ?? new List<ProtocolPublicLink>();
			if (live.Any(l => l.Kind == (int)ProtocolPublicLinkKinds.Category && l.ProtocolCategoryId == categoryId))
				return ProtocolPublicLinkResult.Fail(ProtocolPublicLinkErrors.AlreadyPublic);
			if (live.Count >= MaxLinksPerDepartment)
				return ProtocolPublicLinkResult.Fail(ProtocolPublicLinkErrors.TooManyLinks);

			var link = await InsertAsync(new ProtocolPublicLink
			{
				DepartmentId = reader.DepartmentId,
				Kind = (int)ProtocolPublicLinkKinds.Category,
				ProtocolCategoryId = categoryId,
				EmbedEnabled = embedEnabled,
				EmbedOrigins = string.IsNullOrEmpty(origins) ? null : origins,
				CreatedOn = DateTime.UtcNow,
				CreatedByUserId = reader.UserId
			}, cancellationToken);

			Audit(reader, AuditLogTypes.ProtocolPublicLinkCreated, null, link, null);
			RaiseLinkChanged(reader, link, null, null, category, "Created");
			return new ProtocolPublicLinkResult { Link = link };
		}

		public async Task<ProtocolPublicLinkResult> RevokeAsync(ProtocolReader reader, int protocolPublicLinkId, CancellationToken cancellationToken = default)
		{
			if (reader == null || !reader.MayApprove)
				return ProtocolPublicLinkResult.Fail(ProtocolPublicLinkErrors.NotAuthorized);

			var link = await _links.GetByIdForDepartmentAsync(reader.DepartmentId, protocolPublicLinkId);
			if (link == null || link.DepartmentId != reader.DepartmentId)
				return ProtocolPublicLinkResult.Fail(ProtocolPublicLinkErrors.NotFound);
			if (link.IsRevoked)
				return new ProtocolPublicLinkResult { Link = link };

			var now = DateTime.UtcNow;
			if (await _links.TryRevokeAsync(reader.DepartmentId, protocolPublicLinkId, reader.UserId, now, cancellationToken))
			{
				var before = JsonConvert.SerializeObject(Snapshot(link));
				link.RevokedOn = now;
				link.RevokedByUserId = reader.UserId;
				Audit(reader, AuditLogTypes.ProtocolPublicLinkRevoked, before, link, null);
				// Once: only the request whose revoke stored it raises the event.
				var linked = link.DispatchProtocolId.HasValue ? await _protocolsService.GetProtocolHeaderByIdAsync(link.DispatchProtocolId.Value) : null;
				RaiseLinkChanged(reader, link, linked != null && linked.DepartmentId == reader.DepartmentId ? linked : null, null, null, "Revoked");
			}

			return new ProtocolPublicLinkResult { Link = await _links.GetByIdForDepartmentAsync(reader.DepartmentId, protocolPublicLinkId) ?? link };
		}

		private async Task<ProtocolPublicLink> InsertAsync(ProtocolPublicLink link, CancellationToken cancellationToken)
		{
			// A token collision is astronomically unlikely (192 random bits); retry once with a fresh one rather than fail.
			for (var attempt = 0; ; attempt++)
			{
				link.Token = ProtocolPublicPages.NewToken();
				try
				{
					return await _links.InsertAsync(link, cancellationToken, true);
				}
				catch (Exception) when (attempt == 0)
				{
					link.ProtocolPublicLinkId = 0;
				}
			}
		}

		#endregion Managing links

		#region The public page

		public async Task<PublicProtocolPage> GetPageAsync(string token, string requestedLocale)
		{
			var link = await LiveLinkAsync(token);
			if (link == null)
				return null;

			var department = await _departments.GetDepartmentByIdAsync(link.DepartmentId, false);
			if (department == null)
				return null;

			var page = new PublicProtocolPage { DepartmentName = department.Name, GeneratedOn = DateTime.UtcNow };

			if (link.Kind == (int)ProtocolPublicLinkKinds.Category)
			{
				var categories = (await _library.GetCategoriesAsync(link.DepartmentId) ?? new List<ProtocolCategory>()).Where(c => c.DepartmentId == link.DepartmentId).ToList();
				var category = categories.FirstOrDefault(c => c.ProtocolCategoryId == link.ProtocolCategoryId);
				if (category == null)
					return null;

				page.IsSet = true;
				page.Title = category.Name;
				var within = Descendants(categories, category.ProtocolCategoryId);
				var candidates = (await _protocolsService.GetProtocolSummariesForDepartmentAsync(link.DepartmentId, true) ?? new List<DispatchProtocol>())
					.Where(p => p.DepartmentId == link.DepartmentId && p.CategoryId.HasValue && within.Contains(p.CategoryId.Value) && p.IsPublished && !p.IsLicensedContent)
					.ToList();
				if (candidates.Count == 0)
					return page;

				var audiences = (await _library.GetDepartmentAudiencesAsync(link.DepartmentId) ?? new List<ProtocolAudience>()).ToLookup(a => a.DispatchProtocolId);
				var versions = (await _versions.GetByIdsAsync(link.DepartmentId, candidates.Select(p => p.CurrentVersionId.Value), true) ?? new List<ProtocolVersion>())
					.Where(v => v.DepartmentId == link.DepartmentId).ToDictionary(v => v.ProtocolVersionId);
				var attributions = (await _attributions.GetForSubjectsAsync(link.DepartmentId, ContentAttributionSubjects.ProtocolVersion, versions.Keys) ?? new List<ContentAttribution>())
					.GroupBy(a => a.SubjectId).ToDictionary(g => g.Key, g => g.First());
				var names = categories.ToDictionary(c => c.ProtocolCategoryId, c => c.Name);

				foreach (var protocol in candidates.OrderBy(p => names.TryGetValue(p.CategoryId.Value, out var n) ? n : string.Empty, StringComparer.CurrentCultureIgnoreCase)
					.ThenBy(p => string.IsNullOrWhiteSpace(p.Number) ? p.Code : p.Number, StringComparer.CurrentCultureIgnoreCase)
					.ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
				{
					versions.TryGetValue(protocol.CurrentVersionId.Value, out var version);
					var entry = await ProjectAsync(protocol, version, audiences[protocol.DispatchProtocolId], names.TryGetValue(protocol.CategoryId.Value, out var categoryName) ? categoryName : null,
						version != null && attributions.TryGetValue(version.ProtocolVersionId, out var attribution) ? attribution : null, requestedLocale);
					if (entry != null)
						page.Entries.Add(entry);
				}

				return page;
			}

			var (single, current, audience) = await LoadAsync(link.DepartmentId, link.DispatchProtocolId ?? 0, true);
			if (single == null)
				return null;

			page.Title = single.Name;
			if (link.ProtocolVersionId.HasValue && single.CurrentVersionId != link.ProtocolVersionId)
			{
				// The pinned version is no longer the one in force: say so, show nothing of it or of the newer one.
				page.Superseded = true;
				return page;
			}

			var categoriesForOne = await _library.GetCategoriesAsync(link.DepartmentId) ?? new List<ProtocolCategory>();
			var one = await ProjectAsync(single, current, audience, single.CategoryId.HasValue ? categoriesForOne.FirstOrDefault(c => c.ProtocolCategoryId == single.CategoryId.Value)?.Name : null,
				current == null ? null : await _attributions.GetForSubjectAsync(link.DepartmentId, ContentAttributionSubjects.ProtocolVersion, current.ProtocolVersionId), requestedLocale);
			if (one == null)
				return null;

			page.Entries.Add(one);
			return page;
		}

		public async Task<PublicProtocolEmbedPolicy> GetEmbedPolicyAsync(string token) => ProtocolPublicPages.EmbedPolicy(await LiveLinkAsync(token));

		private async Task<ProtocolPublicLink> LiveLinkAsync(string token)
		{
			if (!ProtocolPublicPages.IsTokenShape(token))
				return null;
			var link = await _links.GetByTokenAsync(token);
			return link == null || link.IsRevoked || !string.Equals(link.Token, token, StringComparison.Ordinal) ? null : link;
		}

		private async Task<PublicProtocolEntry> ProjectAsync(DispatchProtocol protocol, ProtocolVersion version, IEnumerable<IContentAudienceRule> audience, string categoryName,
			ContentAttribution attribution, string requestedLocale)
		{
			if (ProtocolPublicPages.Eligibility(protocol, version, audience) != ProtocolPublicEligibility.Eligible)
				return null;

			// An approved translation in the reader's language when there is one made from this content; otherwise the version itself.
			var localized = string.IsNullOrWhiteSpace(requestedLocale) ? null : await _translations.GetLocalizedAsync(protocol.DepartmentId, version, requestedLocale);
			var html = localized?.ContentHtml ?? version.ContentHtml;
			var locale = localized?.Locale ?? version.Locale;
			return ProtocolPublicPages.Project(protocol, version, audience, categoryName, ((ProtocolKinds)protocol.Kind).ToString(),
				ProtocolContentDocument.SanitizeForOutput(html), locale, attribution);
		}

		#endregion The public page

		private async Task<(DispatchProtocol Protocol, ProtocolVersion Version, List<ProtocolAudience> Audience)> LoadAsync(int departmentId, int protocolId, bool withContent)
		{
			if (departmentId <= 0 || protocolId <= 0)
				return (null, null, null);
			var protocol = await _protocolsService.GetProtocolHeaderByIdAsync(protocolId);
			if (protocol == null || protocol.DepartmentId != departmentId)
				return (null, null, null);
			var version = protocol.CurrentVersionId.HasValue
				? withContent ? await _versions.GetByIdForDepartmentAsync(departmentId, protocol.CurrentVersionId.Value) : await _library.GetVersionAsync(departmentId, protocol.CurrentVersionId.Value)
				: null;
			if (version != null && version.DepartmentId != departmentId)
				version = null;
			var audience = await _library.GetAudienceAsync(departmentId, protocolId) ?? new List<ProtocolAudience>();
			return (protocol, version, audience);
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

		private static object Snapshot(ProtocolPublicLink link) => new
		{
			link.ProtocolPublicLinkId,
			link.Kind,
			link.DispatchProtocolId,
			link.ProtocolCategoryId,
			link.ProtocolVersionId,
			link.EmbedEnabled,
			link.EmbedOrigins,
			link.RevokedOn
		};

		private void Audit(ProtocolReader reader, AuditLogTypes type, string before, ProtocolPublicLink link, ProtocolVersion version)
		{
			try
			{
				_eventAggregator?.SendMessage<AuditEvent>(new AuditEvent
				{
					DepartmentId = reader.DepartmentId,
					UserId = reader.UserId,
					Type = type,
					Before = before,
					After = JsonConvert.SerializeObject(new { Link = Snapshot(link), VersionId = version?.ProtocolVersionId, version?.ContentHash }),
					Successful = true,
					ServerName = Environment.MachineName
				});
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Public protocol link audit failed.");
			}
		}
	}
}
