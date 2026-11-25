using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services.Knowledge;

namespace Resgrid.Services
{
	/// <summary>
	/// The PDF book (P3): a category (with its sub-categories) or the whole library, every readable protocol's version in force, with a
	/// table of contents, each protocol's version stamp, effective date, approver and content short code. Sealed at L3 through
	/// <see cref="IDocumentSealingService"/> when sealing is configured; otherwise the unsealed rendering, recorded "sealing pending" (the
	/// service fails closed, exactly as the single-version PDF does).
	/// </summary>
	public class ProtocolBookService : IProtocolBookService
	{
		public const string BookSchema = "resgrid-protocol-book/1";

		private readonly IProtocolsService _protocolsService;
		private readonly IProtocolAccessService _access;
		private readonly IProtocolVersionsRepository _versions;
		private readonly IProtocolVersionApprovalsRepository _approvals;
		private readonly IProtocolCategoriesRepository _categories;
		private readonly IDepartmentsService _departments;
		private readonly IDocumentSealingService _sealing;
		private readonly IEventAggregator _eventAggregator;
		private readonly IDepartmentSettingsRepository _settings;

		public ProtocolBookService(IProtocolsService protocolsService, IProtocolAccessService access, IProtocolVersionsRepository versions,
			IProtocolVersionApprovalsRepository approvals, IProtocolCategoriesRepository categories, IDepartmentsService departments,
			IDocumentSealingService sealing, IEventAggregator eventAggregator, IDepartmentSettingsRepository settings)
		{
			_settings = settings;
			_protocolsService = protocolsService;
			_access = access;
			_versions = versions;
			_approvals = approvals;
			_categories = categories;
			_departments = departments;
			_sealing = sealing;
			_eventAggregator = eventAggregator;
		}

		public async Task<ProtocolBook> BuildAsync(int departmentId, string userId, int? categoryId, ProtocolBookText text)
		{
			text ??= new ProtocolBookText();
			var book = new ProtocolBook { DepartmentId = departmentId, CategoryId = categoryId, GeneratedOn = DateTime.UtcNow };
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(userId))
				return null;

			var categories = (await _categories.GetForDepartmentAsync(departmentId) ?? new List<ProtocolCategory>()).Where(c => c.DepartmentId == departmentId).ToList();
			var categoryNames = categories.ToDictionary(c => c.ProtocolCategoryId, c => c.Name);
			HashSet<int> wanted = null;
			if (categoryId.HasValue)
			{
				var root = categories.FirstOrDefault(c => c.ProtocolCategoryId == categoryId.Value);
				if (root == null)
					return null;
				wanted = Descendants(categories, root.ProtocolCategoryId);
				book.Title = root.Name;
			}
			else
			{
				book.Title = text.LibraryTitle;
			}

			var reader = await _access.GetReaderAsync(departmentId, userId);
			var all = (await _protocolsService.GetProtocolSummariesForDepartmentAsync(departmentId, true) ?? new List<DispatchProtocol>())
				.Where(p => p.DepartmentId == departmentId && !p.IsDeleted && p.Status == (int)ProtocolStatuses.Published && p.CurrentVersionId.HasValue
					&& (wanted == null || (p.CategoryId.HasValue && wanted.Contains(p.CategoryId.Value))))
				.ToList();
			var readable = await _access.FilterReadableAsync(reader, all) ?? new List<DispatchProtocol>();

			var versions = (await _versions.GetByIdsAsync(departmentId, readable.Select(p => p.CurrentVersionId.Value), true) ?? new List<ProtocolVersion>())
				.Where(v => v.DepartmentId == departmentId && v.Status == (int)ProtocolVersionStatuses.Published).ToDictionary(v => v.ProtocolVersionId);
			var approvals = (await _approvals.GetForVersionsAsync(departmentId, versions.Keys) ?? new List<ProtocolVersionApproval>()).ToLookup(a => a.ProtocolVersionId);
			var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (var person in await _departments.GetAllPersonnelNamesForDepartmentAsync(departmentId) ?? new List<PersonName>())
				if (person != null && !string.IsNullOrWhiteSpace(person.UserId))
					names[person.UserId] = person.Name?.Trim();
			// The department's dosing-tool link (K1 setting 139) on every medication block, as in the reading view; never part of a hash.
			var dosingToolUrl = (await KnowledgeSettingsReader.ReadAsync(_settings, departmentId))?.DosingToolUrl;

			foreach (var protocol in readable)
			{
				if (!versions.TryGetValue(protocol.CurrentVersionId.Value, out var version))
					continue;

				var round = approvals[version.ProtocolVersionId].Where(a => a.Decision == (int)ProtocolApprovalDecisions.Approved).ToList();
				if (round.Count > 0)
				{
					var last = round.Max(a => a.Round);
					round = round.Where(a => a.Round == last).OrderBy(a => a.StepOrder).ToList();
				}
				var approverIds = round.Select(a => a.DecidedByUserId).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
				if (approverIds.Count == 0 && !string.IsNullOrWhiteSpace(version.ApprovedByUserId))
					approverIds.Add(version.ApprovedByUserId);

				book.Entries.Add(new ProtocolBookEntry
				{
					Protocol = protocol,
					Version = version,
					CategoryName = protocol.CategoryId.HasValue && categoryNames.TryGetValue(protocol.CategoryId.Value, out var category) ? category : text.Uncategorized,
					ApproverNames = string.Join(", ", approverIds.Distinct(StringComparer.OrdinalIgnoreCase).Select(id => names.TryGetValue(id, out var name) && !string.IsNullOrWhiteSpace(name) ? name : id)),
					ApprovedOn = round.Count > 0 ? round.Max(a => a.DecidedOn) : version.ApprovedOn,
					BodyHtml = version.IsDocument
						? "<p><em>" + WebUtility.HtmlEncode(text.DocumentBacked) + "</em></p>"
						: ProtocolContentDocument.InjectDosingLinks(ProtocolContentDocument.DisplayHtml(version.ContentHtml), dosingToolUrl, text.DosingToolLink)
				});
			}

			book.Entries = book.Entries
				.OrderBy(e => e.CategoryName, StringComparer.CurrentCultureIgnoreCase)
				.ThenBy(e => string.IsNullOrWhiteSpace(e.Protocol.Number) ? e.Protocol.Code : e.Protocol.Number, StringComparer.CurrentCultureIgnoreCase)
				.ThenBy(e => e.Protocol.Name, StringComparer.CurrentCultureIgnoreCase)
				.ToList();
			book.BookHash = Hash(departmentId, categoryId, book.Entries, CultureInfo.CurrentUICulture.Name);
			book.Html = Render(book, text, (await _departments.GetDepartmentByIdAsync(departmentId, false))?.Name);
			return book;
		}

		public async Task<ProtocolBookExport> ExportPdfAsync(int departmentId, string userId, int? categoryId, ProtocolBookText text, string culture, string pageSize,
			CancellationToken cancellationToken = default)
		{
			var book = await BuildAsync(departmentId, userId, categoryId, text);
			if (book == null)
				return null;

			var subjectId = categoryId.HasValue ? "category-" + categoryId.Value.ToString(CultureInfo.InvariantCulture) : "library";
			var fileName = "protocols-" + subjectId + "-" + book.GeneratedOn.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".pdf";
			var department = await _departments.GetDepartmentByIdAsync(departmentId, false);
			var outcome = await _sealing.SealHtmlAsync(new SealHtmlRequest
			{
				DepartmentId = departmentId,
				SubjectKind = SignatureSubjectKinds.ProtocolBook,
				SubjectId = subjectId,
				RevisionId = book.GeneratedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
				// The seal binds to the book's own hash: the same versions (in the same language) give the same book, and a re-download
				// reuses the sealed copy.
				RevisionSha256 = book.BookHash,
				Title = book.Title,
				FileName = fileName,
				Html = book.Html,
				PageSize = string.IsNullOrWhiteSpace(pageSize) ? "Letter" : pageSize,
				DepartmentName = department?.Name,
				Culture = culture,
				RequestedByUserId = userId
			}, cancellationToken);

			_eventAggregator?.SendMessage<AuditEvent>(new AuditEvent
			{
				DepartmentId = departmentId,
				UserId = userId,
				Type = AuditLogTypes.ProtocolBookExported,
				After = JsonConvert.SerializeObject(new
				{
					Subject = subjectId,
					book.BookHash,
					Protocols = book.Entries.Select(e => new { e.Protocol.DispatchProtocolId, e.Version.ProtocolVersionId, e.Version.ContentHash }).ToList(),
					Sealed = outcome?.Sealed ?? false,
					Pending = outcome?.Pending ?? false
				}),
				Successful = true,
				ServerName = Environment.MachineName
			});

			return new ProtocolBookExport
			{
				Book = book,
				Pdf = outcome?.Bytes,
				Sealed = outcome?.Sealed ?? false,
				SealingPending = outcome == null || !outcome.Sealed,
				FileName = outcome?.Document?.FileName ?? fileName
			};
		}

		private static HashSet<int> Descendants(List<ProtocolCategory> categories, int rootId)
		{
			var set = new HashSet<int> { rootId };
			var added = true;
			while (added)
			{
				added = false;
				foreach (var category in categories)
				{
					if (category.ParentCategoryId.HasValue && set.Contains(category.ParentCategoryId.Value) && set.Add(category.ProtocolCategoryId))
						added = true;
				}
			}

			return set;
		}

		/// <summary>SHA-256 over the book's canonical form: schema, department, category, language and each (protocol, version, content hash).</summary>
		public static string Hash(int departmentId, int? categoryId, IEnumerable<ProtocolBookEntry> entries, string culture)
		{
			var canonical = new StringBuilder(BookSchema).Append('|').Append(departmentId.ToString(CultureInfo.InvariantCulture))
				.Append('|').Append(categoryId?.ToString(CultureInfo.InvariantCulture) ?? "library").Append('|').Append(culture ?? string.Empty);
			foreach (var entry in entries ?? Enumerable.Empty<ProtocolBookEntry>())
				canonical.Append('|').Append(entry.Protocol.DispatchProtocolId.ToString(CultureInfo.InvariantCulture)).Append(':')
					.Append(entry.Version.ProtocolVersionId.ToString(CultureInfo.InvariantCulture)).Append(':').Append(entry.Version.ContentHash ?? string.Empty);
			return SignaturePolicy.HashArtifact(canonical.ToString());
		}

		/// <summary>The book as one HTML document: title page with a TOC linking each protocol, then each protocol on a new page.</summary>
		public static string Render(ProtocolBook book, ProtocolBookText text, string departmentName)
		{
			string E(string value) => WebUtility.HtmlEncode(value ?? string.Empty);
			string Label(ProtocolVersion v) => "v" + v.Major.ToString(CultureInfo.InvariantCulture) + "." + v.Minor.ToString(CultureInfo.InvariantCulture);
			string Date(DateTime? d) => d.HasValue ? d.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "-";

			var html = new StringBuilder("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>").Append(E(book.Title)).Append("</title><style>")
				.Append("body{font-family:Arial,Helvetica,sans-serif;font-size:12px;color:#222;margin:24px}h1{font-size:20px;margin:0 0 6px}")
				.Append(".muted{color:#555}.toc{margin:16px 0;padding:0;list-style:none}.toc li{margin:3px 0}.toc .cat{font-weight:bold;margin-top:10px}")
				.Append(".protocol{page-break-before:always}.stamp{border:1px solid #bbb;padding:6px 8px;margin:6px 0 12px;font-size:11px}")
				.Append("table{border-collapse:collapse}td,th{border:1px solid #bbb;padding:4px 6px;text-align:left;vertical-align:top}")
				.Append(".rgp-block{margin:8px 0}.rgp-caution,.rgp-warning,.rgp-gate{border-left:4px solid #b26a00;padding:4px 8px;background:#fff8e6}")
				.Append(".rgp-warning{border-left-color:#b00020;background:#fdecee}.rgp-gate{border-left-color:#1565c0;background:#e8f1fb}")
				.Append(".rgp-medication{border:1px solid #888;padding:6px 8px}.rgp-variant{border-top:1px dashed #999;margin-top:6px;padding-top:4px}")
				.Append(".rgp-chip{border:1px solid #999;border-radius:8px;padding:0 6px;margin-right:4px;font-size:10px}")
				.Append("</style></head><body>");

			html.Append("<h1>").Append(E(book.Title)).Append("</h1>");
			html.Append("<p class=\"muted\">").Append(E(departmentName)).Append(" · ").Append(E(text.GeneratedOn)).Append(' ')
				.Append(book.GeneratedOn.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)).Append(" UTC · ")
				.Append("<span style=\"font-family:monospace\">").Append(E(SignaturePolicy.ShortCode(book.BookHash))).Append("</span></p>");

			html.Append("<h2>").Append(E(text.Contents)).Append("</h2>");
			if (book.Entries.Count == 0)
				html.Append("<p>").Append(E(text.Empty)).Append("</p>");
			html.Append("<ul class=\"toc\">");
			string lastCategory = null;
			foreach (var entry in book.Entries)
			{
				if (!string.Equals(lastCategory, entry.CategoryName, StringComparison.Ordinal))
				{
					html.Append("<li class=\"cat\">").Append(E(entry.CategoryName)).Append("</li>");
					lastCategory = entry.CategoryName;
				}
				html.Append("<li><a href=\"#protocol-").Append(entry.Protocol.DispatchProtocolId.ToString(CultureInfo.InvariantCulture)).Append("\">")
					.Append(E(Title(entry.Protocol))).Append("</a> <span class=\"muted\">").Append(E(Label(entry.Version))).Append(" · ")
					.Append(E(text.EffectiveOn)).Append(' ').Append(Date(entry.Version.EffectiveOn)).Append("</span></li>");
			}
			html.Append("</ul>");

			foreach (var entry in book.Entries)
			{
				html.Append("<div class=\"protocol\" id=\"protocol-").Append(entry.Protocol.DispatchProtocolId.ToString(CultureInfo.InvariantCulture)).Append("\">");
				html.Append("<h1>").Append(E(Title(entry.Protocol))).Append("</h1>");
				html.Append("<div class=\"stamp\">").Append(E(text.Version)).Append(' ').Append(E(Label(entry.Version)))
					.Append(" · ").Append(E(text.EffectiveOn)).Append(' ').Append(Date(entry.Version.EffectiveOn))
					.Append(" · ").Append(E(text.ApprovedBy)).Append(' ').Append(E(string.IsNullOrWhiteSpace(entry.ApproverNames) ? "-" : entry.ApproverNames))
					.Append(entry.ApprovedOn.HasValue ? " (" + Date(entry.ApprovedOn) + ")" : string.Empty)
					.Append(" · ").Append(E(text.ContentHash)).Append(" <span style=\"font-family:monospace\">").Append(E(SignaturePolicy.ShortCode(entry.Version.ContentHash))).Append("</span></div>");
				html.Append(entry.BodyHtml ?? string.Empty);
				html.Append("</div>");
			}

			html.Append("</body></html>");
			return html.ToString();
		}

		private static string Title(DispatchProtocol protocol)
		{
			var number = string.IsNullOrWhiteSpace(protocol.Number) ? protocol.Code : protocol.Number;
			return (string.IsNullOrWhiteSpace(number) ? string.Empty : number + " ") + protocol.Name;
		}
	}
}
