using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;

namespace Resgrid.Services.ContentSets
{
	/// <summary>
	/// Protocols in a set (Protocols P5, round 10 A3; free for every subscriber). A protocol can be shared when its version in force is an
	/// authored (block or html) version and it is not disabled, deleted or licensed content. The subscriber's copy is a protocol of its own: the
	/// publisher's blocks merged with the subscriber's replacements of the customizable blocks and its local additions
	/// (<see cref="ContentSetMerge"/>), put into force through the protocol library so acknowledgments, search and the knowledge index follow.
	/// Triggers, key questions, audiences and categories are the subscriber's own (they name its call types, groups and roles).
	/// </summary>
	public class ProtocolSetItemSource : IContentSetItemSource
	{
		public const string ProblemNotPublished = "not_published";
		public const string ProblemLicensed = "licensed";
		public const string ProblemDisabled = "disabled";
		public const string ProblemDocument = "document";

		private readonly IProtocolsService _protocols;
		private readonly IProtocolLibraryService _library;

		public ProtocolSetItemSource(IProtocolsService protocols, IProtocolLibraryService library)
		{
			_protocols = protocols;
			_library = library;
		}

		public ContentSetItemKinds Kind => ContentSetItemKinds.Protocol;

		public bool NeedsLms => false;

		public ContentSetKinds SetKind => ContentSetKinds.Protocols;

		/// <summary>Why the protocol cannot be in a set now, or null.</summary>
		public static string Problem(DispatchProtocol protocol, ProtocolVersion current)
		{
			if (protocol == null || protocol.IsDeleted || !protocol.IsPublished || current == null || current.Status != (int)ProtocolVersionStatuses.Published ||
				current.DispatchProtocolId != protocol.DispatchProtocolId || current.DepartmentId != protocol.DepartmentId)
				return ProblemNotPublished;
			if (!ProtocolLicensing.CanShare(protocol))
				return ProblemLicensed;
			if (protocol.IsDisabled)
				return ProblemDisabled;
			if (current.IsDocument)
				return ProblemDocument;
			return null;
		}

		public async Task<List<ContentSetCandidate>> GetCandidatesAsync(int publisherDepartmentId)
		{
			var protocols = (await _protocols.GetProtocolSummariesForDepartmentAsync(publisherDepartmentId) ?? new List<DispatchProtocol>())
				.Where(p => p != null && p.DepartmentId == publisherDepartmentId && !p.IsDeleted).ToList();
			var versions = (await _library.GetDepartmentVersionHeadersAsync(publisherDepartmentId) ?? new List<ProtocolVersion>())
				.Where(v => v.DepartmentId == publisherDepartmentId).ToDictionary(v => v.ProtocolVersionId);

			return protocols.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).Select(p =>
			{
				var current = p.CurrentVersionId.HasValue && versions.TryGetValue(p.CurrentVersionId.Value, out var v) ? v : null;
				return new ContentSetCandidate
				{
					Kind = ContentSetItemKinds.Protocol,
					ItemId = p.DispatchProtocolId,
					Title = p.Name,
					VersionLabel = current?.Label,
					Problem = Problem(p, current)
				};
			}).ToList();
		}

		public async Task<ContentSetSourceItem> DescribeAsync(int publisherDepartmentId, int itemId)
		{
			var protocol = await _protocols.GetProtocolHeaderByIdAsync(itemId);
			if (protocol == null || protocol.DepartmentId != publisherDepartmentId || publisherDepartmentId <= 0)
				return null;

			var current = protocol.CurrentVersionId.HasValue ? await _library.GetVersionAsync(publisherDepartmentId, protocol.CurrentVersionId.Value) : null;
			var item = new ContentSetSourceItem
			{
				Kind = ContentSetItemKinds.Protocol,
				ItemId = protocol.DispatchProtocolId,
				Title = protocol.Name,
				Problem = Problem(protocol, current)
			};
			if (current == null)
				return item;

			item.VersionId = current.ProtocolVersionId;
			item.VersionLabel = current.Label;
			item.Major = current.Major;
			item.EffectiveOn = current.EffectiveOn ?? current.PublishedOn;
			item.ChangeSummary = current.ChangeSummary;
			item.ContentJson = current.ContentJson ?? ProtocolContentDocument.FromStoredHtml(current.ContentHtml).ToJson();
			item.Locale = current.Locale;
			item.Blocks = BlockOptions(ProtocolContentDocument.Parse(item.ContentJson, current.ContentHtml));
			return item;
		}

		/// <summary>The publisher's version a copy carries (the customize page edits against it, not against an update still in the inbox).</summary>
		public async Task<ContentSetSourceItem> DescribeVersionAsync(int publisherDepartmentId, int itemId, int versionId)
		{
			var item = await DescribeAsync(publisherDepartmentId, itemId);
			if (item == null || versionId <= 0 || versionId == item.VersionId)
				return item;

			var version = await _library.GetVersionAsync(publisherDepartmentId, versionId);
			if (version == null || version.DispatchProtocolId != itemId || version.DepartmentId != publisherDepartmentId)
				return item;

			item.VersionId = version.ProtocolVersionId;
			item.VersionLabel = version.Label;
			item.Major = version.Major;
			item.EffectiveOn = version.EffectiveOn ?? version.PublishedOn;
			item.ChangeSummary = version.ChangeSummary;
			item.ContentJson = version.ContentJson ?? ProtocolContentDocument.FromStoredHtml(version.ContentHtml).ToJson();
			item.Locale = version.Locale;
			item.Blocks = BlockOptions(ProtocolContentDocument.Parse(item.ContentJson, version.ContentHtml));
			return item;
		}

		/// <summary>Each top-level block with a short label: its type and the start of its text.</summary>
		public static List<ContentSetBlockOption> BlockOptions(ProtocolContentDocument document) =>
			(document?.Blocks ?? new List<ProtocolContentBlock>()).Where(b => b != null && !string.IsNullOrWhiteSpace(b.Key))
				.GroupBy(b => b.Key, StringComparer.Ordinal).Select(g => g.First())
				.Select(b => new ContentSetBlockOption { Key = b.Key, Label = BlockLabel(b) }).ToList();

		public static string BlockLabel(ProtocolContentBlock block)
		{
			var text = !string.IsNullOrWhiteSpace(block.Text) ? block.Text
				: !string.IsNullOrWhiteSpace(block.Title) ? block.Title
				: !string.IsNullOrWhiteSpace(block.Label) ? block.Label
				: !string.IsNullOrWhiteSpace(block.Html) ? WebUtility.HtmlDecode(StringHelpers.HtmlToPlainText(block.Html) ?? string.Empty)
				: block.Medication?.Drug;
			text = (text ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
			if (text.Length > 80)
				text = text.Substring(0, 80) + "…";
			return string.IsNullOrEmpty(text) ? (block.Type ?? string.Empty) : (block.Type ?? string.Empty) + ": " + text;
		}

		private static string MergedJson(ContentSetCopyContext context, out List<string> dropped)
		{
			var source = ProtocolContentDocument.Parse(context.Source.ContentJson, null);
			var merged = ContentSetMerge.Merge(source, (ContentSetLockModes)context.Item.LockMode, context.Item.CustomizableKeys, context.Local, out dropped);
			return merged.ToJson();
		}

		private static ContentAttribution Attribution(ContentSetCopyContext context, int subscriberDepartmentId) => new ContentAttribution
		{
			DepartmentId = subscriberDepartmentId,
			SubjectKind = (int)ContentAttributionSubjects.ProtocolVersion,
			SourceKind = (int)ContentAttributionSources.ContentSet,
			SourceRef = context.Set.ContentSetId.ToString(CultureInfo.InvariantCulture),
			SourceName = string.IsNullOrWhiteSpace(context.PublisherName) ? context.Set.Name : context.PublisherName + ": " + context.Set.Name,
			Author = context.PublisherName,
			SourceVersion = context.Source.VersionLabel,
			CreatedOn = DateTime.UtcNow,
			CreatedByUserId = context.UserId
		}.Normalize();

		private static string Summary(ContentSetCopyContext context)
		{
			var label = string.IsNullOrWhiteSpace(context.Source.VersionLabel) ? string.Empty : " " + context.Source.VersionLabel;
			var from = string.IsNullOrWhiteSpace(context.PublisherName) ? context.Set.Name : context.PublisherName;
			var summary = string.IsNullOrWhiteSpace(context.Source.ChangeSummary) ? string.Empty : ": " + context.Source.ChangeSummary.Trim();
			return ("[" + from + label + "]" + summary).Trim();
		}

		public async Task<ContentSetCopyOutcome> CreateCopyAsync(ContentSetCopyContext context, CancellationToken cancellationToken = default)
		{
			if (context?.Source == null || context.Source.Problem != null)
				return ContentSetCopyOutcome.Fail(context?.Source?.Problem ?? ProblemNotPublished);

			var publisher = await _protocols.GetProtocolHeaderByIdAsync(context.Source.ItemId);
			if (publisher == null || publisher.DepartmentId != context.Set.DepartmentId)
				return ContentSetCopyOutcome.Fail(ProblemNotPublished);
			var version = await _library.GetVersionAsync(publisher.DepartmentId, context.Source.VersionId);

			var json = MergedJson(context, out _);
			var created = await _library.CreateSetCopyAsync(new ProtocolSetCopyRequest
			{
				DepartmentId = context.SubscriberDepartmentId,
				UserId = context.UserId,
				Name = publisher.Name,
				Code = publisher.Code,
				Kind = publisher.Kind,
				Description = publisher.Description,
				Tags = publisher.Tags,
				ContentJson = json,
				Locale = context.Source.Locale,
				RequiresAcknowledgment = version?.RequiresAcknowledgment ?? ProtocolKindRules.AcknowledgmentByDefault(publisher.Kind),
				ChangeSummary = Summary(context),
				Attribution = Attribution(context, context.SubscriberDepartmentId)
			}, cancellationToken);

			return created.Succeeded
				? new ContentSetCopyOutcome { Succeeded = true, LocalItemId = created.Protocol.DispatchProtocolId, LocalVersionId = created.Version?.ProtocolVersionId }
				: ContentSetCopyOutcome.Fail("lifecycle_" + created.Error.ToString().ToLowerInvariant());
		}

		public async Task<ContentSetCopyOutcome> ApplyVersionAsync(ContentSetCopyContext context, CancellationToken cancellationToken = default)
		{
			if (context?.Source == null || context.Copy == null || context.Source.Problem != null)
				return ContentSetCopyOutcome.Fail(context?.Source?.Problem ?? ProblemNotPublished);

			var version = await _library.GetVersionAsync(context.Set.DepartmentId, context.Source.VersionId);
			var json = MergedJson(context, out _);
			var published = await _library.PublishSetVersionAsync(new ProtocolSetVersionRequest
			{
				DepartmentId = context.SubscriberDepartmentId,
				ProtocolId = context.Copy.LocalItemId,
				UserId = context.UserId,
				ContentJson = json,
				Locale = context.Source.Locale,
				RequiresAcknowledgment = version?.RequiresAcknowledgment ?? true,
				ChangeSummary = Summary(context),
				Major = context.Major,
				Attribution = Attribution(context, context.SubscriberDepartmentId)
			}, cancellationToken);

			return published.Succeeded
				? new ContentSetCopyOutcome { Succeeded = true, LocalItemId = context.Copy.LocalItemId, LocalVersionId = published.Version?.ProtocolVersionId }
				: ContentSetCopyOutcome.Fail("lifecycle_" + published.Error.ToString().ToLowerInvariant());
		}

		public async Task<string> GetLocalHtmlAsync(int subscriberDepartmentId, int localItemId)
		{
			var protocol = await _protocols.GetProtocolHeaderByIdAsync(localItemId);
			if (protocol == null || protocol.DepartmentId != subscriberDepartmentId || !protocol.CurrentVersionId.HasValue)
				return string.Empty;
			return (await _library.GetVersionAsync(subscriberDepartmentId, protocol.CurrentVersionId.Value))?.ContentHtml ?? string.Empty;
		}

		public Task<(string Html, List<string> Dropped)> PreviewAsync(ContentSetCopyContext context)
		{
			if (context?.Source == null)
				return Task.FromResult((string.Empty, new List<string>()));
			var json = MergedJson(context, out var dropped);
			return Task.FromResult((_library.RenderContentJson(json, context.Source.Locale), dropped));
		}

		public async Task<bool> LocalExistsAsync(int subscriberDepartmentId, int localItemId)
		{
			var protocol = await _protocols.GetProtocolHeaderByIdAsync(localItemId);
			return protocol != null && protocol.DepartmentId == subscriberDepartmentId && !protocol.IsDeleted;
		}
	}
}
