using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Services;

namespace Resgrid.Services
{
	/// <summary>
	/// Protocols P5 regional publisher/subscriber (round 10 A3): the library side of a subscriber's managed copy. A copy's versions are not
	/// authored here (the edit lock refuses drafts on it, <see cref="ManagedBySetAsync"/>); the set service hands in the merged document and the
	/// library renders, hashes, numbers and puts it into force through the same activation as every publish, so acknowledgments, caches, search,
	/// the knowledge index and the published hooks all follow.
	/// </summary>
	public partial class ProtocolLibraryService
	{
		/// <summary>The protocol is a copy a subscribed set manages (its content changes only through the set).</summary>
		private async Task<bool> ManagedBySetAsync(int departmentId, int protocolId) =>
			_setLocks != null && await _setLocks.IsManagedCopyAsync(departmentId, ContentSetItemKinds.Protocol, protocolId);

		public string RenderContentJson(string contentJson, string locale)
		{
			var document = PrepareSetDocument(contentJson);
			return RenderContent(document, locale);
		}

		/// <summary>
		/// The merged document as the library stores it: a P3 block document is normalized (block HTML sanitized, anchors per block); a P1 html
		/// document keeps its html blocks (sanitized again: a local replacement is the subscriber's HTML) and is outlined from its text.
		/// </summary>
		private static ProtocolContentDocument PrepareSetDocument(string contentJson)
		{
			var document = ProtocolContentDocument.Parse(contentJson, null);
			if (document.IsBlockModel)
				return ProtocolBlocks.Normalize(document);

			foreach (var block in document.Blocks.Where(b => b != null && string.Equals(b.Type, ProtocolContentDocument.HtmlBlockType, StringComparison.OrdinalIgnoreCase)))
				block.Html = StringHelpers.SanitizeHtmlForStorage(block.Html ?? string.Empty) ?? string.Empty;
			var joined = string.Concat(document.Blocks.Where(b => b != null && string.Equals(b.Type, ProtocolContentDocument.HtmlBlockType, StringComparison.OrdinalIgnoreCase)).Select(b => b.Html));
			document.Anchors = ProtocolContentDocument.Outline(joined, document.Blocks.FirstOrDefault()?.Key ?? "b1");
			return document;
		}

		public async Task<ProtocolLifecycleResult> CreateSetCopyAsync(ProtocolSetCopyRequest request, CancellationToken cancellationToken = default)
		{
			if (request == null || request.DepartmentId <= 0 || string.IsNullOrWhiteSpace(request.UserId) || string.IsNullOrWhiteSpace(request.Name))
				return ProtocolLifecycleResult.Fail(ProtocolLifecycleErrors.InvalidInput);

			var document = PrepareSetDocument(request.ContentJson);
			if (document.Blocks.Count == 0)
				return ProtocolLifecycleResult.Fail(ProtocolLifecycleErrors.EmptyContent);

			var created = await CreateAsync(new ProtocolCreateRequest
			{
				DepartmentId = request.DepartmentId,
				UserId = request.UserId,
				Protocol = new DispatchProtocol
				{
					Name = Truncate(request.Name.Trim(), 100),
					Code = Truncate(request.Code?.Trim(), DispatchProtocol.CodeMaxLength),
					Kind = request.Kind,
					Description = Truncate(request.Description, DispatchProtocol.DescriptionTextMaxLength),
					Tags = request.Tags
				},
				ContentJson = document.ToJson(),
				Locale = request.Locale,
				RequiresAcknowledgment = request.RequiresAcknowledgment,
				Attribution = request.Attribution
			}, cancellationToken);
			if (!created.Succeeded)
				return created;

			return await ActivateSetVersionAsync(created.Protocol, created.Version, null, true, request.ChangeSummary, request.UserId, cancellationToken);
		}

		public async Task<ProtocolLifecycleResult> PublishSetVersionAsync(ProtocolSetVersionRequest request, CancellationToken cancellationToken = default)
		{
			if (request == null || request.DepartmentId <= 0 || request.ProtocolId <= 0 || string.IsNullOrWhiteSpace(request.UserId))
				return ProtocolLifecycleResult.Fail(ProtocolLifecycleErrors.InvalidInput);

			var protocol = await _protocolsService.GetProtocolHeaderByIdAsync(request.ProtocolId);
			if (protocol == null || protocol.DepartmentId != request.DepartmentId || protocol.IsDeleted)
				return ProtocolLifecycleResult.Fail(ProtocolLifecycleErrors.NotFound);

			var document = PrepareSetDocument(request.ContentJson);
			if (document.Blocks.Count == 0)
				return ProtocolLifecycleResult.Fail(ProtocolLifecycleErrors.EmptyContent);
			// P4: a graph only on a Dispatch protocol (the publisher's kind is copied, so this only drops one the kind cannot carry).
			if (protocol.Kind != (int)ProtocolKinds.Dispatch)
				document.Graph = null;

			var settings = await GetSettingsAsync(request.DepartmentId);
			var locale = NormalizeLocale(request.Locale);
			var html = RenderContent(document, settings.ContentLocaleFor(new ProtocolVersion { Locale = locale }));
			var now = DateTime.UtcNow;
			var version = await _versions.InsertAsync(new ProtocolVersion
			{
				DepartmentId = request.DepartmentId,
				DispatchProtocolId = protocol.DispatchProtocolId,
				Status = (int)ProtocolVersionStatuses.Draft,
				ContentJson = document.ToJson(),
				ContentHtml = html,
				ContentHash = ProtocolContentHash.Compute(html),
				RequiresAcknowledgment = request.RequiresAcknowledgment,
				Locale = locale,
				CreatedOn = now,
				CreatedByUserId = request.UserId,
				UpdatedOn = now,
				UpdatedByUserId = request.UserId
			}, cancellationToken, true);

			if (request.Attribution != null)
				await KeepAttributionAsync(request.Attribution, version, request.UserId, now, cancellationToken);

			var current = protocol.CurrentVersionId.HasValue ? await GetVersionAsync(request.DepartmentId, protocol.CurrentVersionId.Value) : null;
			return await ActivateSetVersionAsync(protocol, version, current, request.Major, request.ChangeSummary, request.UserId, cancellationToken);
		}

		/// <summary>Numbers the set version from the copy's own history, records it as approved by the set and puts it into force now.</summary>
		private async Task<ProtocolLifecycleResult> ActivateSetVersionAsync(DispatchProtocol protocol, ProtocolVersion version, ProtocolVersion current, bool major,
			string changeSummary, string userId, CancellationToken cancellationToken)
		{
			var now = DateTime.UtcNow;
			var basis = current?.Status == (int)ProtocolVersionStatuses.Published ? current : LastPublished(await GetVersionsAsync(protocol.DepartmentId, protocol.DispatchProtocolId));
			(version.Major, version.Minor) = ProtocolLifecycle.NextNumber(basis, major);
			version.ChangeSummary = Truncate(string.IsNullOrWhiteSpace(changeSummary) ? null : changeSummary.Trim(), ProtocolVersion.ChangeSummaryMaxLength);
			version.Status = (int)ProtocolVersionStatuses.Approved;
			version.ApprovedOn = now;
			version.ApprovedByUserId = userId;
			version.PublishedByUserId = userId;
			version.EffectiveOn = now;
			if (version.RequiresAcknowledgment && !version.QuizTrainingId.HasValue && protocol.AckTrainingId.HasValue)
				version.QuizTrainingId = protocol.AckTrainingId;

			var published = await ActivateAsync(protocol, version, current, userId, now, cancellationToken);
			if (!published.Succeeded)
				return published;

			Audit(protocol.DepartmentId, userId, AuditLogTypes.ProtocolVersionPublished, null, Snapshot(published.Protocol, published.Version));
			ContentWorkflowContext.Publish(_eventAggregator, ProtocolWorkflowEvent.For(published.Protocol, published.Version, ProtocolWorkflowChanges.Published, userId, now));
			return published;
		}
	}
}
