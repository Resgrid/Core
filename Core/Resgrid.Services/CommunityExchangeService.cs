using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

namespace Resgrid.Services
{
	/// <summary>
	/// The community exchange (Protocols P5 / Training T5, §13a row 42, decision 7c — free): a department submits a protocol or a training
	/// (LMS skill sheets and lesson plans through their own Lms* sources); the source strips department data and the scrubber flags phone
	/// numbers; licensed content is refused; a Resgrid reviewer lists or rejects it; every department can then browse, rate, comment and
	/// import it, the import keeping the listing's attribution. What the reviewer passed is what is imported (the payload hash is checked).
	/// </summary>
	public class CommunityExchangeService : ICommunityExchangeService
	{
		public const string SourceName = "Resgrid community exchange";

		private readonly IReadOnlyDictionary<ExchangeItemKinds, IExchangeItemSource> _sources;
		private readonly IExchangeListingsRepository _listings;
		private readonly IExchangeRatingsRepository _ratings;
		private readonly IExchangeCommentsRepository _comments;
		private readonly IExchangeImportsRepository _imports;
		private readonly IEventAggregator _eventAggregator;

		public CommunityExchangeService(IEnumerable<IExchangeItemSource> sources, IExchangeListingsRepository listings, IExchangeRatingsRepository ratings,
			IExchangeCommentsRepository comments, IExchangeImportsRepository imports, IEventAggregator eventAggregator)
		{
			_sources = (sources ?? Enumerable.Empty<IExchangeItemSource>()).GroupBy(s => s.Kind).ToDictionary(g => g.Key, g => g.First());
			_listings = listings;
			_ratings = ratings;
			_comments = comments;
			_imports = imports;
			_eventAggregator = eventAggregator;
		}

		private static bool IsMember(ExchangeActor actor) => actor != null && actor.IsCurrentMember && actor.DepartmentId > 0 && !string.IsNullOrWhiteSpace(actor.UserId);

		private static bool IsReviewer(ExchangeActor actor) => actor != null && actor.IsReviewer && !string.IsNullOrWhiteSpace(actor.UserId);

		public IReadOnlyList<ExchangeItemKinds> GetKinds() => _sources.Keys.OrderBy(k => (int)k).ToList();

		public bool CanSubmitAny(ExchangeActor actor) => IsMember(actor) && _sources.Values.Any(s => s.CanSubmit(actor));

		public async Task<List<ExchangeSourceItem>> GetSubmittableAsync(ExchangeActor actor, ExchangeItemKinds kind)
		{
			if (!IsMember(actor) || !_sources.TryGetValue(kind, out var source) || !source.CanSubmit(actor))
				return new List<ExchangeSourceItem>();
			return await source.GetSubmittableAsync(actor) ?? new List<ExchangeSourceItem>();
		}

		public async Task<ExchangeResult> PreviewSubmissionAsync(ExchangeActor actor, ExchangeItemKinds kind, string sourceRef)
		{
			if (!IsMember(actor))
				return ExchangeResult.Fail(ExchangeErrors.NotAuthorized);
			if (!_sources.TryGetValue(kind, out var source))
				return ExchangeResult.Fail(ExchangeErrors.KindUnavailable);
			if (!source.CanSubmit(actor))
				return ExchangeResult.Fail(ExchangeErrors.NotAuthorized);

			var prepared = await source.PrepareAsync(actor, sourceRef);
			if (prepared == null)
				return ExchangeResult.Fail(ExchangeErrors.NotFound);
			if (prepared.Error == ExchangeErrors.None)
				prepared.PreviewHtml = source.PreviewHtml(prepared.PayloadJson);
			return new ExchangeResult { Error = prepared.Error, Prepared = prepared };
		}

		public async Task<ExchangeResult> SubmitAsync(ExchangeActor actor, ExchangeSubmitRequest request, CancellationToken cancellationToken = default)
		{
			if (request == null || !Enum.IsDefined(typeof(ExchangeItemKinds), request.ItemKind))
				return ExchangeResult.Fail(ExchangeErrors.InvalidInput);
			if (!request.Attested)
				return ExchangeResult.Fail(ExchangeErrors.AttestationRequired);
			if (!ContentLicenses.IsContributable(request.License))
				return ExchangeResult.Fail(ExchangeErrors.LicenseRequired);

			var preview = await PreviewSubmissionAsync(actor, (ExchangeItemKinds)request.ItemKind, request.SourceRef);
			if (!preview.Succeeded)
				return preview;

			var prepared = preview.Prepared;
			if (string.IsNullOrEmpty(prepared.PayloadJson) || prepared.PayloadJson.Length > ExchangeRules.MaxPayloadLength)
				return ExchangeResult.Fail(ExchangeErrors.InvalidInput);

			var summary = string.IsNullOrWhiteSpace(request.Summary) ? prepared.Summary : new ExchangeScrubber(actor.DepartmentName).Text(request.Summary.Trim());
			var now = DateTime.UtcNow;
			var listing = await _listings.InsertAsync(new ExchangeListing
			{
				DepartmentId = actor.DepartmentId,
				ItemKind = request.ItemKind,
				SourceRef = Cut(prepared.SourceRef, ExchangeListing.SourceRefMaxLength),
				Title = Cut(prepared.Title, ExchangeListing.TitleMaxLength) ?? "-",
				Summary = Cut(summary, ExchangeListing.SummaryMaxLength),
				Tags = Cut(prepared.Tags, ExchangeListing.TagsMaxLength),
				VersionLabel = Cut(prepared.VersionLabel, ExchangeListing.VersionLabelMaxLength),
				PayloadJson = prepared.PayloadJson,
				PayloadHash = ExchangeRules.Hash(prepared.PayloadJson),
				License = request.License,
				AttributionName = Cut(string.IsNullOrWhiteSpace(request.AttributionName) ? actor.DepartmentName : request.AttributionName.Trim(), ExchangeListing.AttributionMaxLength),
				ReviewFlagsJson = prepared.Flags.Count == 0 ? null : JsonConvert.SerializeObject(prepared.Flags),
				Status = (int)ExchangeListingStatuses.PendingReview,
				SubmittedOn = now,
				SubmittedByUserId = actor.UserId,
				AttestationVersion = ExchangeRules.AttestationVersion
			}, cancellationToken, true);

			Audit(actor.DepartmentId, actor.UserId, AuditLogTypes.ExchangeListingSubmitted, listing, new { Flags = prepared.Flags.Count });
			return new ExchangeResult { Listing = listing, Prepared = prepared };
		}

		public async Task<ExchangeResult> WithdrawAsync(ExchangeActor actor, int exchangeListingId, CancellationToken cancellationToken = default)
		{
			if (!IsMember(actor))
				return ExchangeResult.Fail(ExchangeErrors.NotAuthorized);
			var listing = await _listings.GetHeaderAsync(exchangeListingId);
			if (listing == null || listing.DepartmentId != actor.DepartmentId)
				return ExchangeResult.Fail(ExchangeErrors.NotFound);
			if (!actor.IsDepartmentAdmin && !string.Equals(listing.SubmittedByUserId, actor.UserId, StringComparison.OrdinalIgnoreCase) && !CanSubmitKind(actor, listing.ItemKind))
				return ExchangeResult.Fail(ExchangeErrors.NotAuthorized);
			if (listing.Status != (int)ExchangeListingStatuses.PendingReview && listing.Status != (int)ExchangeListingStatuses.Listed)
				return ExchangeResult.Fail(ExchangeErrors.WrongStatus);

			var expected = listing.Status;
			listing.Status = (int)ExchangeListingStatuses.Withdrawn;
			listing.WithdrawnOn = DateTime.UtcNow;
			if (!await _listings.TryUpdateStatusAsync(listing, expected, cancellationToken))
				return ExchangeResult.Fail(ExchangeErrors.WrongStatus);

			Audit(actor.DepartmentId, actor.UserId, AuditLogTypes.ExchangeListingWithdrawn, listing, null);
			return new ExchangeResult { Listing = listing };
		}

		public Task<(List<ExchangeListing> Items, int Total)> BrowseAsync(ExchangeBrowseQuery query)
		{
			query ??= new ExchangeBrowseQuery();
			if (query.ItemKind.HasValue && !_sources.ContainsKey((ExchangeItemKinds)query.ItemKind.Value))
				return Task.FromResult((new List<ExchangeListing>(), 0));
			return _listings.BrowseAsync(query);
		}

		public async Task<ExchangeListingView> GetListingAsync(ExchangeActor actor, int exchangeListingId)
		{
			if (!IsMember(actor) && !IsReviewer(actor))
				return null;

			var listing = await _listings.GetWithPayloadAsync(exchangeListingId);
			if (listing == null)
				return null;

			var own = IsMember(actor) && listing.DepartmentId == actor.DepartmentId;
			if (!listing.IsListed && !own && !IsReviewer(actor))
				return null;

			_sources.TryGetValue((ExchangeItemKinds)listing.ItemKind, out var source);
			var view = new ExchangeListingView
			{
				Listing = listing,
				IsOwnDepartment = own,
				PreviewHtml = source?.PreviewHtml(listing.PayloadJson) ?? string.Empty,
				Comments = await _comments.GetForListingAsync(listing.ExchangeListingId, IsReviewer(actor)) ?? new List<ExchangeComment>(),
				ImportNeedsLms = source?.NeedsLms ?? false
			};

			if (IsMember(actor))
			{
				view.MyStars = (await _ratings.GetForListingAndUserAsync(listing.ExchangeListingId, actor.UserId))?.Stars;
				view.CanImport = listing.IsListed && source != null && await source.CanImportAsync(actor);
				view.AlreadyImported = (await _imports.GetForDepartmentAsync(actor.DepartmentId) ?? new List<ExchangeImport>()).Any(i => i.ExchangeListingId == listing.ExchangeListingId);
			}

			// The payload stays on the server; the view carries its preview only.
			listing.PayloadJson = null;
			return view;
		}

		public async Task<List<ExchangeListing>> GetMySubmissionsAsync(ExchangeActor actor) =>
			IsMember(actor) ? await _listings.GetForDepartmentAsync(actor.DepartmentId) ?? new List<ExchangeListing>() : new List<ExchangeListing>();

		public async Task<List<ExchangeListing>> GetReviewQueueAsync(ExchangeActor actor) =>
			IsReviewer(actor) ? await _listings.GetPendingReviewAsync(200) ?? new List<ExchangeListing>() : new List<ExchangeListing>();

		public async Task<ExchangeResult> DecideAsync(ExchangeActor actor, int exchangeListingId, bool approve, string note, CancellationToken cancellationToken = default)
		{
			if (!IsReviewer(actor))
				return ExchangeResult.Fail(ExchangeErrors.NotAuthorized);
			note = Cut(note, ExchangeListing.ReviewNoteMaxLength);
			if (!approve && string.IsNullOrWhiteSpace(note))
				return ExchangeResult.Fail(ExchangeErrors.InvalidInput);

			var listing = await _listings.GetWithPayloadAsync(exchangeListingId);
			if (listing == null)
				return ExchangeResult.Fail(ExchangeErrors.NotFound);
			if (listing.Status != (int)ExchangeListingStatuses.PendingReview)
				return ExchangeResult.Fail(ExchangeErrors.WrongStatus);
			if (!string.Equals(ExchangeRules.Hash(listing.PayloadJson), listing.PayloadHash, StringComparison.OrdinalIgnoreCase))
				return ExchangeResult.Fail(ExchangeErrors.PayloadChanged);

			var now = DateTime.UtcNow;
			listing.PayloadJson = null;
			listing.Status = approve ? (int)ExchangeListingStatuses.Listed : (int)ExchangeListingStatuses.Rejected;
			listing.ReviewedOn = now;
			listing.ReviewedByUserId = actor.UserId;
			listing.ReviewNote = note;
			listing.ListedOn = approve ? now : (DateTime?)null;
			if (!await _listings.TryUpdateStatusAsync(listing, (int)ExchangeListingStatuses.PendingReview, cancellationToken))
				return ExchangeResult.Fail(ExchangeErrors.WrongStatus);

			Audit(listing.DepartmentId, actor.UserId, AuditLogTypes.ExchangeListingDecided, listing, new { Approved = approve });
			ContentWorkflowContext.Publish(_eventAggregator, ReviewedEvent(listing, now));
			return new ExchangeResult { Listing = listing };
		}

		/// <summary>
		/// Workflow trigger 257 for the submitting department after a reviewer's decision was stored (once: the status compare-and-set).
		/// The review note and the reviewer (a Resgrid operator, not a member) stay out of the payload.
		/// </summary>
		public static ExchangeListingWorkflowEvent ReviewedEvent(ExchangeListing listing, DateTime utcNow) => new ExchangeListingWorkflowEvent
		{
			DepartmentId = listing.DepartmentId,
			ListingId = listing.ExchangeListingId,
			ItemKind = Enum.IsDefined(typeof(ExchangeItemKinds), listing.ItemKind) ? ((ExchangeItemKinds)listing.ItemKind).ToString() : null,
			ListingTitle = listing.Title,
			VersionLabel = listing.VersionLabel,
			Decision = Enum.IsDefined(typeof(ExchangeListingStatuses), listing.Status) ? ((ExchangeListingStatuses)listing.Status).ToString() : null,
			UserId = listing.SubmittedByUserId,
			SubmittedOn = listing.SubmittedOn,
			ReviewedOn = listing.ReviewedOn,
			OccurredOn = utcNow
		};

		public async Task<ExchangeResult> RemoveAsync(ExchangeActor actor, int exchangeListingId, string note, CancellationToken cancellationToken = default)
		{
			if (!IsReviewer(actor))
				return ExchangeResult.Fail(ExchangeErrors.NotAuthorized);
			note = Cut(note, ExchangeListing.ReviewNoteMaxLength);
			if (string.IsNullOrWhiteSpace(note))
				return ExchangeResult.Fail(ExchangeErrors.InvalidInput);

			var listing = await _listings.GetHeaderAsync(exchangeListingId);
			if (listing == null)
				return ExchangeResult.Fail(ExchangeErrors.NotFound);
			if (listing.Status != (int)ExchangeListingStatuses.Listed)
				return ExchangeResult.Fail(ExchangeErrors.WrongStatus);

			listing.Status = (int)ExchangeListingStatuses.Removed;
			listing.ReviewedOn = DateTime.UtcNow;
			listing.ReviewedByUserId = actor.UserId;
			listing.ReviewNote = note;
			if (!await _listings.TryUpdateStatusAsync(listing, (int)ExchangeListingStatuses.Listed, cancellationToken))
				return ExchangeResult.Fail(ExchangeErrors.WrongStatus);

			Audit(listing.DepartmentId, actor.UserId, AuditLogTypes.ExchangeListingDecided, listing, new { Removed = true });
			ContentWorkflowContext.Publish(_eventAggregator, ReviewedEvent(listing, listing.ReviewedOn ?? DateTime.UtcNow));
			return new ExchangeResult { Listing = listing };
		}

		public async Task<ExchangeResult> RateAsync(ExchangeActor actor, int exchangeListingId, int stars, CancellationToken cancellationToken = default)
		{
			if (!IsMember(actor))
				return ExchangeResult.Fail(ExchangeErrors.NotAuthorized);
			if (stars < ExchangeRules.MinStars || stars > ExchangeRules.MaxStars)
				return ExchangeResult.Fail(ExchangeErrors.InvalidInput);

			var listing = await _listings.GetHeaderAsync(exchangeListingId);
			if (listing == null || !listing.IsListed)
				return ExchangeResult.Fail(listing == null ? ExchangeErrors.NotFound : ExchangeErrors.WrongStatus);
			if (listing.DepartmentId == actor.DepartmentId)
				return ExchangeResult.Fail(ExchangeErrors.OwnListing);

			var now = DateTime.UtcNow;
			var rating = await _ratings.GetForListingAndUserAsync(exchangeListingId, actor.UserId);
			if (rating != null)
			{
				rating.Stars = stars;
				rating.UpdatedOn = now;
				await _ratings.UpdateAsync(rating, cancellationToken, true);
			}
			else
			{
				await _ratings.InsertAsync(new ExchangeRating { ExchangeListingId = exchangeListingId, DepartmentId = actor.DepartmentId, UserId = actor.UserId, Stars = stars, CreatedOn = now },
					cancellationToken, true);
			}

			await _listings.RefreshRatingAsync(exchangeListingId, cancellationToken);
			return new ExchangeResult { Listing = await _listings.GetHeaderAsync(exchangeListingId) };
		}

		public async Task<ExchangeResult> CommentAsync(ExchangeActor actor, int exchangeListingId, string body, CancellationToken cancellationToken = default)
		{
			if (!IsMember(actor))
				return ExchangeResult.Fail(ExchangeErrors.NotAuthorized);
			var text = body?.Trim();
			if (string.IsNullOrEmpty(text) || text.Length > ExchangeComment.BodyMaxLength)
				return ExchangeResult.Fail(ExchangeErrors.InvalidInput);

			var listing = await _listings.GetHeaderAsync(exchangeListingId);
			if (listing == null || !listing.IsListed)
				return ExchangeResult.Fail(listing == null ? ExchangeErrors.NotFound : ExchangeErrors.WrongStatus);

			// Plain text: markup is shown as typed (the view encodes it), never rendered.
			await _comments.InsertAsync(new ExchangeComment
			{
				ExchangeListingId = exchangeListingId,
				DepartmentId = actor.DepartmentId,
				UserId = actor.UserId,
				AuthorName = Cut(actor.DepartmentName, ExchangeComment.AuthorNameMaxLength),
				Body = text,
				CreatedOn = DateTime.UtcNow
			}, cancellationToken, true);
			return new ExchangeResult { Listing = listing };
		}

		public async Task<ExchangeResult> HideCommentAsync(ExchangeActor actor, int exchangeCommentId, CancellationToken cancellationToken = default)
		{
			var comment = await _comments.GetCommentAsync(exchangeCommentId);
			if (comment == null)
				return ExchangeResult.Fail(ExchangeErrors.NotFound);
			var author = IsMember(actor) && comment.DepartmentId == actor.DepartmentId && string.Equals(comment.UserId, actor.UserId, StringComparison.OrdinalIgnoreCase);
			if (!author && !IsReviewer(actor))
				return ExchangeResult.Fail(ExchangeErrors.NotAuthorized);

			if (await _comments.TryHideAsync(exchangeCommentId, actor.UserId, DateTime.UtcNow, cancellationToken))
				Audit(comment.DepartmentId, actor.UserId, AuditLogTypes.ExchangeCommentHidden, null, new { comment.ExchangeCommentId, comment.ExchangeListingId, ByReviewer = !author });
			return new ExchangeResult();
		}

		public async Task<ExchangeResult> ImportAsync(ExchangeActor actor, int exchangeListingId, CancellationToken cancellationToken = default)
		{
			if (!IsMember(actor))
				return ExchangeResult.Fail(ExchangeErrors.NotAuthorized);

			var listing = await _listings.GetWithPayloadAsync(exchangeListingId);
			if (listing == null)
				return ExchangeResult.Fail(ExchangeErrors.NotFound);
			if (!listing.IsListed)
				return ExchangeResult.Fail(ExchangeErrors.WrongStatus);
			if (!_sources.TryGetValue((ExchangeItemKinds)listing.ItemKind, out var source))
				return ExchangeResult.Fail(ExchangeErrors.KindUnavailable);
			if (!await source.CanImportAsync(actor))
				return ExchangeResult.Fail(source.NeedsLms ? ExchangeErrors.LmsRequired : ExchangeErrors.NotAuthorized);
			if (!string.Equals(ExchangeRules.Hash(listing.PayloadJson), listing.PayloadHash, StringComparison.OrdinalIgnoreCase))
				return ExchangeResult.Fail(ExchangeErrors.PayloadChanged);

			var attribution = AttributionFor(listing, actor);
			int? created;
			try
			{
				created = await source.ImportAsync(actor, listing, attribution, cancellationToken);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Community exchange import failed.");
				created = null;
			}
			if (!created.HasValue || created.Value <= 0)
				return ExchangeResult.Fail(ExchangeErrors.ImportFailed);

			await _imports.InsertAsync(new ExchangeImport
			{
				ExchangeListingId = listing.ExchangeListingId,
				DepartmentId = actor.DepartmentId,
				UserId = actor.UserId,
				ItemKind = listing.ItemKind,
				CreatedItemId = created.Value,
				ImportedOn = DateTime.UtcNow
			}, cancellationToken, true);
			await _listings.IncrementImportCountAsync(listing.ExchangeListingId, cancellationToken);

			listing.PayloadJson = null;
			Audit(actor.DepartmentId, actor.UserId, AuditLogTypes.ExchangeListingImported, listing, new { CreatedItemId = created.Value });
			return new ExchangeResult { Listing = listing, CreatedItemId = created };
		}

		/// <summary>The attribution an import of <paramref name="listing"/> keeps: the exchange, the contributor, the licence and the listing's version.</summary>
		public static ContentAttribution AttributionFor(ExchangeListing listing, ExchangeActor actor) => new ContentAttribution
		{
			DepartmentId = actor.DepartmentId,
			SourceKind = (int)ContentAttributionSources.CommunityExchange,
			SourceRef = listing.ExchangeListingId.ToString(CultureInfo.InvariantCulture),
			SourceName = SourceName,
			Author = listing.AttributionName,
			License = listing.License,
			LicenseUrl = ContentLicenses.UrlFor(listing.License),
			SourceVersion = listing.VersionLabel,
			CreatedOn = DateTime.UtcNow,
			CreatedByUserId = actor.UserId
		};

		private bool CanSubmitKind(ExchangeActor actor, int kind) => _sources.TryGetValue((ExchangeItemKinds)kind, out var source) && source.CanSubmit(actor);

		private static string Cut(string value, int max)
		{
			if (string.IsNullOrWhiteSpace(value))
				return null;
			var trimmed = value.Trim();
			return trimmed.Length > max ? trimmed.Substring(0, max) : trimmed;
		}

		private void Audit(int departmentId, string userId, AuditLogTypes type, ExchangeListing listing, object extra)
		{
			try
			{
				_eventAggregator?.SendMessage<AuditEvent>(new AuditEvent
				{
					DepartmentId = departmentId,
					UserId = userId,
					Type = type,
					After = JsonConvert.SerializeObject(new
					{
						Listing = listing == null ? null : new { listing.ExchangeListingId, listing.ItemKind, listing.SourceRef, listing.Title, listing.Status, listing.License, listing.PayloadHash },
						Extra = extra
					}),
					Successful = true,
					ServerName = Environment.MachineName
				});
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Community exchange audit failed.");
			}
		}
	}

	/// <summary>
	/// Protocols on the exchange: published, not licensed (<see cref="ProtocolLicensing.CanShare"/>), authored (not document-backed) versions only,
	/// as an open-format document of one item with the department's configuration (triggers, audience, number, category) and references
	/// (internal links, files, inventory links, gate contacts) taken out. Submitting is a publish outside the department: approvers and admins.
	/// Importing creates a draft through the library's own import: authors and admins.
	/// </summary>
	public class ProtocolExchangeItemSource : IExchangeItemSource
	{
		private readonly IProtocolsService _protocolsService;
		private readonly IProtocolLibraryService _library;
		private readonly IProtocolAccessService _access;

		public ProtocolExchangeItemSource(IProtocolsService protocolsService, IProtocolLibraryService library, IProtocolAccessService access)
		{
			_protocolsService = protocolsService;
			_library = library;
			_access = access;
		}

		public ExchangeItemKinds Kind => ExchangeItemKinds.Protocol;

		public bool NeedsLms => false;

		public bool CanSubmit(ExchangeActor actor) => actor != null && actor.IsCurrentMember && (actor.IsDepartmentAdmin || actor.CanApproveProtocols);

		public Task<bool> CanImportAsync(ExchangeActor actor) => Task.FromResult(actor != null && actor.IsCurrentMember && (actor.IsDepartmentAdmin || actor.CanAuthorProtocols));

		/// <summary>Why a protocol cannot go to the exchange (or None).</summary>
		public static ExchangeErrors Blocked(DispatchProtocol protocol, ProtocolVersion current)
		{
			if (protocol == null || protocol.IsDeleted || protocol.IsBulletin)
				return ExchangeErrors.NotFound;
			// The P4 licensing guardrail: licensed content (MPDS, IntelliComm…) never leaves its department.
			if (!ProtocolLicensing.CanShare(protocol))
				return ExchangeErrors.LicensedContent;
			if (!protocol.IsPublished || current == null || current.Status != (int)ProtocolVersionStatuses.Published)
				return ExchangeErrors.NotPublished;
			if (current.IsDocument)
				return ExchangeErrors.DocumentBacked;
			return ExchangeErrors.None;
		}

		public async Task<List<ExchangeSourceItem>> GetSubmittableAsync(ExchangeActor actor)
		{
			var protocols = (await _protocolsService.GetProtocolSummariesForDepartmentAsync(actor.DepartmentId) ?? new List<DispatchProtocol>())
				.Where(p => p.DepartmentId == actor.DepartmentId && !p.IsDeleted && !p.IsBulletin).ToList();
			var headers = (await _library.GetDepartmentVersionHeadersAsync(actor.DepartmentId) ?? new List<ProtocolVersion>()).ToDictionary(v => v.ProtocolVersionId);
			return protocols.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).Select(p =>
			{
				var current = p.CurrentVersionId.HasValue && headers.TryGetValue(p.CurrentVersionId.Value, out var v) ? v : null;
				return new ExchangeSourceItem
				{
					ItemKind = (int)ExchangeItemKinds.Protocol,
					SourceRef = p.DispatchProtocolId.ToString(CultureInfo.InvariantCulture),
					Title = (string.IsNullOrWhiteSpace(p.Number) ? p.Code : p.Number) + " " + p.Name,
					VersionLabel = current?.Label,
					Blocked = Blocked(p, current)
				};
			}).ToList();
		}

		public async Task<ExchangePreparedItem> PrepareAsync(ExchangeActor actor, string sourceRef)
		{
			if (!int.TryParse(sourceRef, NumberStyles.Integer, CultureInfo.InvariantCulture, out var protocolId) || protocolId <= 0)
				return new ExchangePreparedItem { Error = ExchangeErrors.NotFound };

			var protocol = await _protocolsService.GetProtocolByIdAsync(protocolId);
			if (protocol == null || protocol.DepartmentId != actor.DepartmentId)
				return new ExchangePreparedItem { Error = ExchangeErrors.NotFound };
			var current = protocol.CurrentVersionId.HasValue ? await _library.GetVersionAsync(actor.DepartmentId, protocol.CurrentVersionId.Value) : null;
			var blocked = Blocked(protocol, current);
			if (blocked != ExchangeErrors.None)
				return new ExchangePreparedItem { Error = blocked };

			var scrubber = new ExchangeScrubber(actor.DepartmentName);
			if ((protocol.Triggers?.Count ?? 0) > 0 || !string.IsNullOrWhiteSpace(protocol.Number) || (await _library.GetAudienceAsync(actor.DepartmentId, protocolId) ?? new List<ProtocolAudience>()).Count > 0)
				scrubber.Flag(ExchangeReviewFlagKinds.ConfigurationRemoved);
			if ((protocol.Attachments?.Count ?? 0) > 0)
				scrubber.Flag(ExchangeReviewFlagKinds.AttachmentRemoved);

			// Content from the template library or the exchange keeps crediting where it came from.
			var earlier = await _library.GetAttributionAsync(actor.DepartmentId, current.ProtocolVersionId);
			var item = new ProtocolExchangeItem
			{
				Code = protocol.Code,
				Name = scrubber.Text(protocol.Name),
				Kind = ((ProtocolKinds)protocol.Kind).ToString(),
				Tags = scrubber.Text(protocol.Tags),
				Description = scrubber.Html(protocol.Description),
				MinimumWeight = protocol.MinimumWeight,
				RequiresAcknowledgment = current.RequiresAcknowledgment,
				ExcludeFromAi = protocol.ExcludeFromAi,
				IsLicensedContent = false,
				VersionLabel = current.Label,
				Locale = current.Locale,
				Content = scrubber.Document(ProtocolContentDocument.Parse(current.ContentJson, current.ContentHtml)),
				Questions = (protocol.Questions ?? new List<DispatchProtocolQuestion>()).OrderBy(q => q.DispatchProtocolQuestionId).Select(q => new ProtocolExchangeQuestion
				{
					Question = scrubber.Text(q.Question),
					Answers = (q.Answers ?? new List<DispatchProtocolQuestionAnswer>()).OrderBy(a => a.DispatchProtocolQuestionAnswerId)
						.Select(a => new ProtocolExchangeAnswer { Answer = scrubber.Text(a.Answer), Weight = a.Weight }).ToList()
				}).ToList(),
				Attribution = earlier == null ? null : new ProtocolExchangeAttribution
				{
					Source = earlier.SourceName, Author = earlier.Author, License = earlier.License, LicenseUrl = earlier.LicenseUrl, SourceVersion = earlier.SourceVersion
				}
			};

			var document = new ProtocolExchangeDocument { Schema = ProtocolExchangeDocument.SchemaId, ExportedOn = DateTime.UtcNow };
			document.Protocols.Add(item);
			return new ExchangePreparedItem
			{
				ItemKind = (int)ExchangeItemKinds.Protocol,
				SourceRef = protocolId.ToString(CultureInfo.InvariantCulture),
				Title = item.Name,
				Summary = StringHelpers.HtmlToPlainText(item.Description)?.Trim(),
				Tags = item.Tags,
				VersionLabel = current.Label,
				PayloadJson = document.ToJson(),
				Flags = scrubber.Flags
			};
		}

		public string PreviewHtml(string payloadJson)
		{
			var item = ProtocolExchangeDocument.Parse(payloadJson)?.Protocols?.FirstOrDefault();
			if (item?.Content == null)
				return string.Empty;
			return ProtocolContentDocument.DisplayHtml(item.Content.Render());
		}

		public async Task<int?> ImportAsync(ExchangeActor actor, ExchangeListing listing, ContentAttribution attribution, CancellationToken cancellationToken = default)
		{
			var reader = await _access.GetReaderAsync(actor.DepartmentId, actor.UserId);
			if (reader == null || !reader.MayAuthor)
				return null;

			var document = ProtocolExchangeDocument.Parse(listing.PayloadJson);
			if (document?.Protocols == null || document.Protocols.Count == 0)
				return null;

			foreach (var item in document.Protocols)
			{
				// Whatever the contributor's own source was, the importer's copy credits the exchange listing (and names the earlier author).
				var basedOn = item.Attribution?.Author;
				item.Attribution = new ProtocolExchangeAttribution
				{
					Source = attribution.SourceName,
					Author = string.IsNullOrWhiteSpace(basedOn) || string.Equals(basedOn, attribution.Author, StringComparison.OrdinalIgnoreCase)
						? attribution.Author
						: attribution.Author + " (based on " + basedOn + ")",
					License = attribution.License,
					LicenseUrl = attribution.LicenseUrl,
					SourceVersion = attribution.SourceVersion
				};
				item.IsLicensedContent = false;
				item.Triggers = new List<ProtocolExchangeTrigger>();
				item.Audience = new List<ProtocolExchangeAudience>();
				item.Number = null;
			}
			document.AttributionSource = ContentAttributionSources.CommunityExchange;
			document.AttributionSourceRef = attribution.SourceRef;

			var result = await _library.ImportAsync(reader, document, cancellationToken);
			return result?.CreatedProtocolIds?.FirstOrDefault() is int id && id > 0 ? id : (int?)null;
		}
	}

	/// <summary>
	/// Baseline Trainings on the exchange (free): name, description, text, pass mark, hours, objectives, tags and the quiz, never assignees, dates,
	/// audiences, CE provider data or attachments. A training marked "no AI" is treated as licensed content (decision 14: the per-item flag is for
	/// licensed content) and refused. Submitting and importing need the Training create right (or department admin).
	/// </summary>
	public class TrainingExchangeItemSource : IExchangeItemSource
	{
		private readonly ITrainingService _trainings;
		private readonly IContentAttributionsRepository _attributions;

		public TrainingExchangeItemSource(ITrainingService trainings, IContentAttributionsRepository attributions)
		{
			_trainings = trainings;
			_attributions = attributions;
		}

		public ExchangeItemKinds Kind => ExchangeItemKinds.Training;

		public bool NeedsLms => false;

		public bool CanSubmit(ExchangeActor actor) => actor != null && actor.IsCurrentMember && (actor.IsDepartmentAdmin || actor.CanCreateTrainings);

		public Task<bool> CanImportAsync(ExchangeActor actor) => Task.FromResult(CanSubmit(actor));

		public static ExchangeErrors Blocked(Training training) =>
			training == null ? ExchangeErrors.NotFound : training.ExcludeFromAi ? ExchangeErrors.LicensedContent : ExchangeErrors.None;

		public async Task<List<ExchangeSourceItem>> GetSubmittableAsync(ExchangeActor actor)
		{
			return (await _trainings.GetAllTrainingsForDepartmentAsync(actor.DepartmentId) ?? new List<Training>())
				.Where(t => t.DepartmentId == actor.DepartmentId && !t.IsArchived)
				.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)
				.Select(t => new ExchangeSourceItem
				{
					ItemKind = (int)ExchangeItemKinds.Training,
					SourceRef = t.TrainingId.ToString(CultureInfo.InvariantCulture),
					Title = t.Name,
					Blocked = Blocked(t)
				}).ToList();
		}

		public async Task<ExchangePreparedItem> PrepareAsync(ExchangeActor actor, string sourceRef)
		{
			if (!int.TryParse(sourceRef, NumberStyles.Integer, CultureInfo.InvariantCulture, out var trainingId) || trainingId <= 0)
				return new ExchangePreparedItem { Error = ExchangeErrors.NotFound };
			var training = await _trainings.GetTrainingByIdAsync(trainingId);
			if (training == null || training.DepartmentId != actor.DepartmentId)
				return new ExchangePreparedItem { Error = ExchangeErrors.NotFound };
			var blocked = Blocked(training);
			if (blocked != ExchangeErrors.None)
				return new ExchangePreparedItem { Error = blocked };

			var scrubber = new ExchangeScrubber(actor.DepartmentName);
			if ((training.Attachments?.Count ?? 0) > 0)
				scrubber.Flag(ExchangeReviewFlagKinds.AttachmentRemoved);
			if (!string.IsNullOrWhiteSpace(training.UsersToAdd) || !string.IsNullOrWhiteSpace(training.GroupsToAdd) || !string.IsNullOrWhiteSpace(training.RolesToAdd) ||
				(training.Audiences?.Count ?? 0) > 0)
				scrubber.Flag(ExchangeReviewFlagKinds.ConfigurationRemoved);

			var payload = new ExchangeTrainingPayload
			{
				Name = scrubber.Text(training.Name),
				Description = scrubber.Html(StringHelpers.SanitizeHtmlForStorage(training.Description ?? string.Empty)),
				TrainingText = scrubber.Html(StringHelpers.SanitizeHtmlForStorage(training.TrainingText ?? string.Empty)),
				MinimumScore = training.MinimumScore,
				Hours = training.Hours,
				Objectives = scrubber.Text(training.Objectives),
				Tags = scrubber.Text(training.Tags),
				Questions = (training.Questions ?? new List<TrainingQuestion>()).OrderBy(q => q.TrainingQuestionId).Select(q => new ExchangeTrainingQuestion
				{
					Question = scrubber.Text(q.Question),
					Type = q.Type,
					Answers = (q.Answers ?? new List<TrainingQuestionAnswer>()).OrderBy(a => a.TrainingQuestionAnswerId)
						.Select(a => new ExchangeTrainingAnswer { Answer = scrubber.Text(a.Answer), Correct = a.Correct }).ToList()
				}).ToList()
			};

			return new ExchangePreparedItem
			{
				ItemKind = (int)ExchangeItemKinds.Training,
				SourceRef = trainingId.ToString(CultureInfo.InvariantCulture),
				Title = payload.Name,
				Summary = StringHelpers.HtmlToPlainText(payload.Description)?.Trim(),
				Tags = payload.Tags,
				PayloadJson = JsonConvert.SerializeObject(payload, Formatting.None),
				Flags = scrubber.Flags
			};
		}

		public static ExchangeTrainingPayload Parse(string payloadJson)
		{
			if (string.IsNullOrWhiteSpace(payloadJson))
				return null;
			try
			{
				var payload = JsonConvert.DeserializeObject<ExchangeTrainingPayload>(payloadJson, new JsonSerializerSettings { MaxDepth = 32 });
				return payload != null && string.Equals(payload.Format, ExchangeTrainingPayload.FormatName, StringComparison.Ordinal) ? payload : null;
			}
			catch (JsonException)
			{
				return null;
			}
		}

		public string PreviewHtml(string payloadJson)
		{
			var payload = Parse(payloadJson);
			if (payload == null)
				return string.Empty;
			var html = new StringBuilder();
			html.Append(StringHelpers.SanitizeHtmlForDisplay(payload.Description ?? string.Empty));
			html.Append(StringHelpers.SanitizeHtmlForDisplay(payload.TrainingText ?? string.Empty));
			if (payload.Questions.Count > 0)
			{
				// The questions without their answers: the preview never gives away the quiz key.
				html.Append("<ol class=\"rgx-questions\">");
				foreach (var question in payload.Questions.Where(q => q != null))
					html.Append("<li>").Append(WebUtility.HtmlEncode(question.Question ?? string.Empty)).Append("</li>");
				html.Append("</ol>");
			}
			return html.ToString();
		}

		public async Task<int?> ImportAsync(ExchangeActor actor, ExchangeListing listing, ContentAttribution attribution, CancellationToken cancellationToken = default)
		{
			var payload = Parse(listing.PayloadJson);
			if (payload == null || string.IsNullOrWhiteSpace(payload.Name))
				return null;

			var now = DateTime.UtcNow;
			var training = new Training
			{
				DepartmentId = actor.DepartmentId,
				CreatedByUserId = actor.UserId,
				CreatedOn = now,
				Name = payload.Name.Trim().Length > 500 ? payload.Name.Trim().Substring(0, 500) : payload.Name.Trim(),
				Description = payload.Description,
				TrainingText = payload.TrainingText,
				MinimumScore = payload.MinimumScore,
				Hours = payload.Hours,
				Objectives = payload.Objectives,
				Tags = payload.Tags,
				Questions = new Collection<TrainingQuestion>()
			};
			foreach (var question in payload.Questions.Where(q => q != null && !string.IsNullOrWhiteSpace(q.Question)))
			{
				training.Questions.Add(new TrainingQuestion
				{
					Question = question.Question.Trim(),
					Type = question.Type,
					Answers = (question.Answers ?? new List<ExchangeTrainingAnswer>()).Where(a => a != null && !string.IsNullOrWhiteSpace(a.Answer))
						.Select(a => new TrainingQuestionAnswer { Answer = a.Answer.Trim(), Correct = a.Correct }).ToList()
				});
			}

			var saved = await _trainings.SaveAsync(training, cancellationToken);
			if (saved == null || saved.TrainingId <= 0)
				return null;

			try
			{
				var row = attribution.CopyFor(ContentAttributionSubjects.Training, saved.TrainingId, actor.UserId, now);
				row.DepartmentId = actor.DepartmentId;
				await _attributions.SaveForSubjectAsync(row, cancellationToken);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "Training attribution could not be kept.");
			}

			return saved.TrainingId;
		}
	}
}
