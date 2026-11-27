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
using Resgrid.Localization.Areas.User.Signatures;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Helpers;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Services.Records
{
	/// <summary>The subject of a partner's deployment acceptance (round 10 A3, kind 27): "{originDepartmentId}:{RmsExternalOrderFillId}".</summary>
	public static class RecordDeploymentPartnerSubject
	{
		public static string For(int originDepartmentId, string fillId) => originDepartmentId.ToString(CultureInfo.InvariantCulture) + ":" + fillId;

		public static bool TryParse(string subjectId, out int originDepartmentId, out string fillId)
		{
			originDepartmentId = 0;
			fillId = null;
			var separator = (subjectId ?? string.Empty).IndexOf(':');
			if (separator <= 0 || separator == subjectId.Length - 1)
				return false;
			fillId = subjectId.Substring(separator + 1);
			return int.TryParse(subjectId.Substring(0, separator), NumberStyles.None, CultureInfo.InvariantCulture, out originDepartmentId) && originDepartmentId > 0 &&
				!string.IsNullOrWhiteSpace(fillId) && fillId.Length <= 64;
		}

		/// <summary>
		/// Whether the partner may see and accept the fill over its link now: the order names the link and the partner, the link is enabled and its
		/// parties are exactly the two departments, the fill was accepted by the department (and not declined or deleted) and names the same partner.
		/// </summary>
		public static bool IsRoutable(RmsExternalOrder order, RmsExternalOrderFill fill, DepartmentLink link, int partnerDepartmentId) =>
			order != null && fill != null && !order.DeletedOn.HasValue && !fill.DeletedOn.HasValue && fill.RmsExternalOrderId == order.RmsExternalOrderId &&
			fill.DepartmentId == order.DepartmentId && order.PartnerDepartmentLinkId.HasValue && link != null && link.DepartmentLinkId == order.PartnerDepartmentLinkId.Value &&
			link.LinkEnabled && order.PartnerDepartmentId == partnerDepartmentId && fill.PartnerDepartmentId == partnerDepartmentId && partnerDepartmentId != order.DepartmentId &&
			DepartmentLinkSharing.GetOtherDepartmentId(link, order.DepartmentId) == partnerDepartmentId &&
			fill.Status != (int)RmsDeploymentFillStatus.Requested && fill.Status != (int)RmsDeploymentFillStatus.Declined;

		/// <summary>
		/// The canonical text the partner's signature binds to: who sends, the order and request it fills, and the resource and position (never the
		/// roster, travel or notes, which may carry protected data). Stable once the fill is accepted.
		/// </summary>
		public static string Canonical(RmsExternalOrder order, RmsExternalOrderFill fill, string sendingDepartmentName) =>
			string.Join("\n", "deployment-acceptance/1", order.DepartmentId.ToString(CultureInfo.InvariantCulture), sendingDepartmentName ?? string.Empty,
				order.SourceSystem ?? string.Empty, order.OrderNumber ?? string.Empty, fill.RmsExternalOrderFillId, fill.RequestNumber ?? string.Empty, fill.FillNumber ?? string.Empty,
				fill.ResourceKind ?? string.Empty, fill.ResourceType ?? string.Empty, fill.Position ?? string.Empty,
				fill.FilledOn?.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture) ?? string.Empty);

		public static string Title(RmsExternalOrder order, RmsExternalOrderFill fill) =>
			string.Join(" / ", new[] { order?.OrderNumber, fill?.RequestNumber, fill?.ResourceType ?? fill?.ResourceKind }.Where(v => !string.IsNullOrWhiteSpace(v)));
	}

	/// <summary>A linked partner's acceptance of a Records deployment fill (round 10 A3). See <see cref="IRecordDeploymentPartnerService"/>.</summary>
	public class RecordDeploymentPartnerService : IRecordDeploymentPartnerService
	{
		private readonly IRmsExternalOrdersRepository _orders;
		private readonly IRmsExternalOrderFillsRepository _fills;
		private readonly IDepartmentLinksService _links;
		private readonly IDepartmentsService _departments;
		private readonly IRecordsAuthorizationService _authorization;
		// The signing layer reaches this service through its subject sources: Lazy keeps construction acyclic.
		private readonly Lazy<ISignatureRequestService> _requests;
		private readonly IEventAggregator _events;

		public RecordDeploymentPartnerService(IRmsExternalOrdersRepository orders, IRmsExternalOrderFillsRepository fills, IDepartmentLinksService links, IDepartmentsService departments,
			IRecordsAuthorizationService authorization, Lazy<ISignatureRequestService> requests, IEventAggregator events)
		{
			_orders = orders;
			_fills = fills;
			_links = links;
			_departments = departments;
			_authorization = authorization;
			_requests = requests;
			_events = events;
		}

		public async Task<List<DepartmentLink>> GetPartnerLinksAsync(int departmentId) =>
			((await _links.GetAllLinksForDepartmentAsync(departmentId)) ?? new List<DepartmentLink>()).Where(l => l != null && l.LinkEnabled).ToList();

		public async Task<RecordDeploymentPartnerErrors> SetPartnerAsync(int departmentId, string userId, string orderId, int? departmentLinkId, CancellationToken cancellationToken = default)
		{
			var order = await _orders.GetByIdForDepartmentAsync(departmentId, orderId, false);
			if (order == null || order.DeletedOn.HasValue)
				return RecordDeploymentPartnerErrors.NotFound;
			if (!await _authorization.CanUserViewRecordAsync(userId, order.RecordId, departmentId) || !await _authorization.HasPermissionAsync(userId, departmentId, PermissionTypes.CreateRecord))
				return RecordDeploymentPartnerErrors.NotAuthorized;
			if (order.Status == (int)RmsExternalOrderStatus.ClosedOut)
				return RecordDeploymentPartnerErrors.ClosedOut;

			int? partnerId = null;
			if (departmentLinkId.HasValue && departmentLinkId.Value > 0)
			{
				var link = await _links.GetLinkByIdAsync(departmentLinkId.Value);
				var other = link == null ? 0 : DepartmentLinkSharing.GetOtherDepartmentId(link, departmentId) ?? 0;
				if (link == null || !link.LinkEnabled || other <= 0 || other == departmentId)
					return RecordDeploymentPartnerErrors.NotLinked;
				partnerId = other;
			}
			else
				departmentLinkId = null;

			if (order.PartnerDepartmentLinkId == departmentLinkId && order.PartnerDepartmentId == partnerId)
				return RecordDeploymentPartnerErrors.None;

			// Only the partner columns are written (never the whole order, whose artifact is not loaded here), at the version read.
			if (!await _orders.TrySetPartnerAsync(departmentId, order.RmsExternalOrderId, departmentLinkId, partnerId, userId, order.RowVersion, cancellationToken))
				return RecordDeploymentPartnerErrors.Conflict;
			var previousPartner = order.PartnerDepartmentId;
			order.PartnerDepartmentLinkId = departmentLinkId;
			order.PartnerDepartmentId = partnerId;
			order.RowVersion += 1;

			// A changed or removed partner voids what the previous partner had not signed yet; signed acceptances stay (they bind their own text).
			foreach (var fill in (await _fills.GetForOrderAsync(departmentId, order.RmsExternalOrderId) ?? Enumerable.Empty<RmsExternalOrderFill>())
				.Where(f => f.PartnerSignStatus == (int)RmsDeploymentPartnerSignStatuses.Requested))
				await LapseAsync(order, fill, "partner changed", cancellationToken);
			Audit(departmentId, userId, new { order.RmsExternalOrderId, previousPartner, order.PartnerDepartmentId, order.PartnerDepartmentLinkId, action = "partner set" });

			// Fills already accepted go to the new partner at once.
			if (partnerId.HasValue)
				foreach (var fill in (await _fills.GetForOrderAsync(departmentId, order.RmsExternalOrderId) ?? Enumerable.Empty<RmsExternalOrderFill>())
					.Where(f => f.Status != (int)RmsDeploymentFillStatus.Requested && f.Status != (int)RmsDeploymentFillStatus.Declined &&
						f.PartnerSignStatus != (int)RmsDeploymentPartnerSignStatuses.Signed && f.PartnerSignStatus != (int)RmsDeploymentPartnerSignStatuses.Requested))
					await RequestAsync(departmentId, fill.RmsExternalOrderFillId, userId, cancellationToken);
			return RecordDeploymentPartnerErrors.None;
		}

		public async Task<RmsExternalOrderFill> RequestAsync(int departmentId, string fillId, string userId, CancellationToken cancellationToken = default)
		{
			var fill = await _fills.GetByIdForDepartmentAsync(departmentId, fillId);
			if (fill == null || fill.PartnerSignStatus == (int)RmsDeploymentPartnerSignStatuses.Requested || fill.PartnerSignStatus == (int)RmsDeploymentPartnerSignStatuses.Signed)
				return fill;
			var order = await _orders.GetByIdForDepartmentAsync(departmentId, fill.RmsExternalOrderId, false);
			if (order?.PartnerDepartmentLinkId == null || !order.PartnerDepartmentId.HasValue)
				return fill;
			var link = await _links.GetLinkByIdAsync(order.PartnerDepartmentLinkId.Value);
			fill.PartnerDepartmentId = order.PartnerDepartmentId;
			if (!RecordDeploymentPartnerSubject.IsRoutable(order, fill, link, order.PartnerDepartmentId.Value))
				return fill;

			var result = await _requests.Value.CreateMultiSignerAsync(new MultiSignerRequestInput
			{
				DepartmentId = order.PartnerDepartmentId.Value,
				SubjectKind = SignatureSubjectKinds.RecordDeploymentPartnerAcceptance,
				SubjectId = RecordDeploymentPartnerSubject.For(departmentId, fill.RmsExternalOrderFillId),
				Ordered = true,
				ExternalChannel = SignatureRequestChannels.InPerson,
				ExpiresInDays = 14,
				RequestedByUserId = userId,
				Steps = new List<SignatureStepInput>
				{
					new SignatureStepInput
					{
						Kind = SignatureSignerKinds.Member,
						Role = SignatureSignerRoles.Member,
						Order = 1,
						// Any of the partner's department admins, at L2 (e-Sign §20: L2 for a partner agency that is on Resgrid).
						Eligibility = new SignerEligibility { DepartmentAdmins = true },
						Level = SignatureLevels.StepUp,
						StepLabel = "Partner department admin"
					}
				}
			}, cancellationToken);
			if (result?.Request == null)
			{
				Logging.LogError($"Deployment fill {fill.RmsExternalOrderFillId}: the partner's acceptance request was refused ({result?.Refusal}).");
				return fill;
			}

			fill.PartnerSignStatus = (int)RmsDeploymentPartnerSignStatuses.Requested;
			fill.PartnerSignRequestId = result.Request.SignatureRequestId;
			fill.PartnerSignRequestedOn = DateTime.UtcNow;
			fill.PartnerSignedOn = null;
			fill.PartnerSignedByUserId = null;
			await _fills.UpdatePartnerSignAsync(fill, cancellationToken);
			Audit(departmentId, userId, new { fill.RmsExternalOrderFillId, fill.PartnerDepartmentId, fill.PartnerSignRequestId, action = "partner acceptance requested" });
			Raise(order, fill, RecordDeploymentPartnerChanges.Requested, userId);
			return fill;
		}

		public async Task<RmsExternalOrderFill> RefreshAsync(RmsExternalOrderFill fill, CancellationToken cancellationToken = default)
		{
			if (fill == null || fill.PartnerSignStatus != (int)RmsDeploymentPartnerSignStatuses.Requested || !fill.PartnerSignRequestId.HasValue || !fill.PartnerDepartmentId.HasValue)
				return fill;
			try
			{
				var request = await _requests.Value.GetAsync(fill.PartnerDepartmentId.Value, fill.PartnerSignRequestId.Value);
				if (request == null || request.IsOpen)
					return fill;
				var order = await _orders.GetByIdForDepartmentAsync(fill.DepartmentId, fill.RmsExternalOrderId, false);
				if (request.Status == (int)SignatureRequestStatuses.Completed)
					return await MarkSignedAsync(order, fill, null, request.CompletedOn ?? DateTime.UtcNow, cancellationToken);

				fill.PartnerSignStatus = (int)(request.Status == (int)SignatureRequestStatuses.Declined ? RmsDeploymentPartnerSignStatuses.Declined : RmsDeploymentPartnerSignStatuses.Lapsed);
				await _fills.UpdatePartnerSignAsync(fill, cancellationToken);
				Raise(order, fill, RecordDeploymentPartnerChanges.Decided, null);
				return fill;
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, "The partner's deployment acceptance state could not be read.");
				return fill;
			}
		}

		/// <summary>The partner's step was signed (the request completed): the fill records who accepted the assignment and when.</summary>
		public async Task<RmsExternalOrderFill> MarkSignedAsync(RmsExternalOrder order, RmsExternalOrderFill fill, string partnerUserId, DateTime signedOnUtc, CancellationToken cancellationToken)
		{
			if (fill == null || fill.PartnerSignStatus == (int)RmsDeploymentPartnerSignStatuses.Signed)
				return fill;
			fill.PartnerSignStatus = (int)RmsDeploymentPartnerSignStatuses.Signed;
			fill.PartnerSignedOn = signedOnUtc;
			fill.PartnerSignedByUserId = partnerUserId ?? fill.PartnerSignedByUserId;
			await _fills.UpdatePartnerSignAsync(fill, cancellationToken);
			Audit(fill.DepartmentId, partnerUserId, new { fill.RmsExternalOrderFillId, fill.PartnerDepartmentId, fill.PartnerSignRequestId, action = "partner accepted" });
			Raise(order, fill, RecordDeploymentPartnerChanges.Decided, partnerUserId);
			return fill;
		}

		public async Task<List<RecordDeploymentPartnerItem>> GetAwaitingAsync(int partnerDepartmentId)
		{
			var items = new List<RecordDeploymentPartnerItem>();
			if (partnerDepartmentId <= 0)
				return items;
			foreach (var fill in await _fills.GetPartnerSignRequestedAsync(partnerDepartmentId) ?? Enumerable.Empty<RmsExternalOrderFill>())
			{
				var order = await _orders.GetByIdForDepartmentAsync(fill.DepartmentId, fill.RmsExternalOrderId, false);
				var link = order?.PartnerDepartmentLinkId == null ? null : await _links.GetLinkByIdAsync(order.PartnerDepartmentLinkId.Value);
				if (!RecordDeploymentPartnerSubject.IsRoutable(order, fill, link, partnerDepartmentId) || !fill.PartnerSignRequestId.HasValue)
					continue;
				items.Add(new RecordDeploymentPartnerItem
				{
					OriginDepartmentId = fill.DepartmentId,
					OriginDepartmentName = (await _departments.GetDepartmentByIdAsync(fill.DepartmentId, false))?.Name,
					Title = RecordDeploymentPartnerSubject.Title(order, fill),
					SignatureRequestId = fill.PartnerSignRequestId.Value,
					RequestedOn = fill.PartnerSignRequestedOn
				});
			}

			return items;
		}

		public async Task<int> LapseForFillAsync(int departmentId, string fillId, CancellationToken cancellationToken = default)
		{
			var fill = await _fills.GetByIdForDepartmentAsync(departmentId, fillId);
			if (fill == null || fill.PartnerSignStatus != (int)RmsDeploymentPartnerSignStatuses.Requested)
				return 0;
			var order = await _orders.GetByIdForDepartmentAsync(departmentId, fill.RmsExternalOrderId, false);
			await LapseAsync(order, fill, "fill declined", cancellationToken);
			return 1;
		}

		public async Task<int> LapseForLinkAsync(int? departmentLinkId, CancellationToken cancellationToken = default)
		{
			var lapsed = 0;
			foreach (var fill in await _fills.GetAllPartnerSignRequestedAsync(2000) ?? Enumerable.Empty<RmsExternalOrderFill>())
			{
				try
				{
					var order = await _orders.GetByIdForDepartmentAsync(fill.DepartmentId, fill.RmsExternalOrderId, false);
					if (departmentLinkId.HasValue && order?.PartnerDepartmentLinkId != departmentLinkId)
						continue;
					var link = order?.PartnerDepartmentLinkId == null ? null : await _links.GetLinkByIdAsync(order.PartnerDepartmentLinkId.Value);
					if (fill.PartnerDepartmentId.HasValue && RecordDeploymentPartnerSubject.IsRoutable(order, fill, link, fill.PartnerDepartmentId.Value))
						continue;
					await LapseAsync(order, fill, "link", cancellationToken);
					lapsed++;
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					Logging.LogException(ex, $"Deployment fill {fill.RmsExternalOrderFillId}: the lapsed partner acceptance could not be voided.");
				}
			}

			return lapsed;
		}

		private async Task LapseAsync(RmsExternalOrder order, RmsExternalOrderFill fill, string reason, CancellationToken cancellationToken)
		{
			if (fill.PartnerSignRequestId.HasValue && fill.PartnerDepartmentId.HasValue)
			{
				try
				{
					await _requests.Value.VoidAsync(fill.PartnerDepartmentId.Value, fill.PartnerSignRequestId.Value, null, "The partner acceptance is no longer routed (" + reason + ").", cancellationToken);
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					throw;
				}
				catch (Exception ex)
				{
					// The partner department (or its request) may be gone: the fill still lapses, so the sweep does not retry it forever.
					Logging.LogException(ex, $"Deployment fill {fill.RmsExternalOrderFillId}: the partner's request could not be voided.");
				}
			}
			fill.PartnerSignStatus = (int)RmsDeploymentPartnerSignStatuses.Lapsed;
			await _fills.UpdatePartnerSignAsync(fill, cancellationToken);
			Audit(fill.DepartmentId, null, new { fill.RmsExternalOrderFillId, fill.PartnerDepartmentId, fill.PartnerSignRequestId, action = "partner acceptance lapsed", reason });
			Raise(order, fill, RecordDeploymentPartnerChanges.Decided, null);
		}

		/// <summary>Both departments' workflows hear the change (296 requested; 297 decided with State Signed, Declined or Lapsed).</summary>
		private void Raise(RmsExternalOrder order, RmsExternalOrderFill fill, RecordDeploymentPartnerChanges change, string userId)
		{
			foreach (var departmentId in new[] { fill.DepartmentId, fill.PartnerDepartmentId ?? 0 }.Where(d => d > 0).Distinct())
				ContentWorkflowContext.Publish(_events, new RecordDeploymentPartnerWorkflowEvent
				{
					DepartmentId = departmentId,
					OriginDepartmentId = fill.DepartmentId,
					PartnerDepartmentId = fill.PartnerDepartmentId ?? 0,
					OrderNumber = order?.OrderNumber,
					RequestNumber = fill.RequestNumber,
					ResourceType = fill.ResourceType ?? fill.ResourceKind,
					SignatureRequestId = fill.PartnerSignRequestId ?? 0,
					Change = change,
					State = ((RmsDeploymentPartnerSignStatuses)fill.PartnerSignStatus).ToString(),
					ActorUserId = userId,
					OccurredOn = DateTime.UtcNow
				});
		}

		private void Audit(int departmentId, string userId, object after) =>
			_events?.SendMessage<AuditEvent>(new AuditEvent
			{
				DepartmentId = departmentId,
				UserId = userId,
				Type = AuditLogTypes.RecordDeploymentPartnerChanged,
				After = JsonConvert.SerializeObject(after),
				Successful = true,
				ServerName = Environment.MachineName
			});
	}

	/// <summary>
	/// The partner's deployment acceptance subject (round 10 A3, kind 27): the origin's fill read over the link, bound to
	/// <see cref="RecordDeploymentPartnerSubject.Canonical"/>. Described in the partner department only while the link routes it.
	/// </summary>
	public class RecordDeploymentPartnerAcceptanceSource : ISignatureSubjectSource
	{
		private readonly IRmsExternalOrdersRepository _orders;
		private readonly IRmsExternalOrderFillsRepository _fills;
		private readonly IDepartmentLinksService _links;
		private readonly IDepartmentsService _departments;
		private readonly Lazy<IRecordDeploymentPartnerService> _partners;

		public RecordDeploymentPartnerAcceptanceSource(IRmsExternalOrdersRepository orders, IRmsExternalOrderFillsRepository fills, IDepartmentLinksService links, IDepartmentsService departments,
			Lazy<IRecordDeploymentPartnerService> partners)
		{
			_orders = orders;
			_fills = fills;
			_links = links;
			_departments = departments;
			_partners = partners;
		}

		public SignatureSubjectKinds Kind => SignatureSubjectKinds.RecordDeploymentPartnerAcceptance;

		/// <summary>The origin's order and fill when the partner department may accept it over the link now; nulls otherwise.</summary>
		internal async Task<(RmsExternalOrder Order, RmsExternalOrderFill Fill)> ReadAsync(int partnerDepartmentId, string subjectId)
		{
			if (partnerDepartmentId <= 0 || !RecordDeploymentPartnerSubject.TryParse(subjectId, out var originId, out var fillId) || originId == partnerDepartmentId)
				return (null, null);
			var fill = await _fills.GetByIdForDepartmentAsync(originId, fillId);
			if (fill == null || (fill.PartnerSignStatus != (int)RmsDeploymentPartnerSignStatuses.Requested && fill.PartnerSignStatus != (int)RmsDeploymentPartnerSignStatuses.Signed))
				return (null, null);
			var order = await _orders.GetByIdForDepartmentAsync(originId, fill.RmsExternalOrderId, false);
			var link = order?.PartnerDepartmentLinkId == null ? null : await _links.GetLinkByIdAsync(order.PartnerDepartmentLinkId.Value);
			return RecordDeploymentPartnerSubject.IsRoutable(order, fill, link, partnerDepartmentId) ? (order, fill) : (null, null);
		}

		public async Task<SignatureSubjectInfo> DescribeAsync(int departmentId, string subjectId, string userId)
		{
			var (order, fill) = await ReadAsync(departmentId, subjectId);
			if (order == null)
				return null;
			var sender = (await _departments.GetDepartmentByIdAsync(order.DepartmentId, false))?.Name;
			var culture = CultureInfo.CurrentUICulture.Name;
			var html = new StringBuilder("<dl>");
			void Row(string key, string value)
			{
				if (!string.IsNullOrWhiteSpace(value))
					html.Append("<dt>").Append(WebUtility.HtmlEncode(SignatureTexts.Get(key, culture))).Append("</dt><dd>").Append(WebUtility.HtmlEncode(value)).Append("</dd>");
			}
			Row("PartnerDeploymentFrom", sender);
			Row("PartnerDeploymentOrder", string.Join(" ", new[] { order.SourceSystem, order.OrderNumber }.Where(v => !string.IsNullOrWhiteSpace(v))));
			Row("PartnerDeploymentRequest", string.Join(" / ", new[] { fill.RequestNumber, fill.FillNumber }.Where(v => !string.IsNullOrWhiteSpace(v))));
			Row("PartnerDeploymentResource", string.Join(" / ", new[] { fill.ResourceKind, fill.ResourceType }.Where(v => !string.IsNullOrWhiteSpace(v))));
			Row("PartnerDeploymentPosition", fill.Position);
			Row("PartnerDeploymentFilledOn", fill.FilledOn?.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture));
			html.Append("</dl>");
			return new SignatureSubjectInfo
			{
				Kind = Kind,
				SubjectId = subjectId,
				Title = RecordDeploymentPartnerSubject.Title(order, fill),
				RevisionId = "fill-" + fill.RmsExternalOrderFillId,
				RevisionSha256 = SignaturePolicy.HashArtifact(RecordDeploymentPartnerSubject.Canonical(order, fill, sender)),
				DocumentHtml = html.ToString()
			};
		}

		public async Task OnSignedAsync(SignatureRequestRecord request, SignatureRequestSigner signer, string actingUserId, CancellationToken cancellationToken = default)
		{
			if (request == null)
				return;
			var (order, fill) = await ReadAsync(request.DepartmentId, request.SubjectId);
			if (fill == null || fill.PartnerSignRequestId != request.SignatureRequestId)
				return;
			if (_partners.Value is RecordDeploymentPartnerService service)
				await service.MarkSignedAsync(order, fill, signer?.UserId ?? actingUserId, signer?.SignedOn ?? DateTime.UtcNow, cancellationToken);
		}
	}

	/// <summary>Who may open the partner's deployment acceptance request (kind 27): the partner department's admins, while the link routes it.</summary>
	public class RecordDeploymentPartnerAcceptanceAccessRule : ISignatureSubjectAccessRule
	{
		private readonly RecordDeploymentPartnerAcceptanceSource _source;
		private readonly IDepartmentsService _departments;

		public RecordDeploymentPartnerAcceptanceAccessRule(IRmsExternalOrdersRepository orders, IRmsExternalOrderFillsRepository fills, IDepartmentLinksService links,
			IDepartmentsService departments, Lazy<IRecordDeploymentPartnerService> partners)
		{
			_source = new RecordDeploymentPartnerAcceptanceSource(orders, fills, links, departments, partners);
			_departments = departments;
		}

		public SignatureSubjectKinds Kind => SignatureSubjectKinds.RecordDeploymentPartnerAcceptance;

		public async Task<bool> CanActOnAsync(int departmentId, string userId, string subjectId)
		{
			if (string.IsNullOrWhiteSpace(userId) || (await _source.ReadAsync(departmentId, subjectId)).Order == null)
				return false;
			var department = await _departments.GetDepartmentByIdAsync(departmentId, false);
			return department != null && (string.Equals(department.ManagingUserId, userId, StringComparison.OrdinalIgnoreCase) || department.IsUserAnAdmin(userId));
		}
	}
}
