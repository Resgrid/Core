using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Invoicing;
using Resgrid.Model.Repositories;
using Resgrid.Model.Search;
using Resgrid.Model.Services;

namespace Resgrid.Services.Search
{
	/// <summary>
	/// Builds and stores the safe search projection for each Tier 1 entity (plan R3). The allowlist is decided here, per
	/// family, against the protected-field catalog: a column the catalog protects (call name/nature/address/incident
	/// number, every contact field, document name/description/filename, message subject/body, member identification
	/// number) is projected only when Advanced Data Protection is not enforced for the department (R2.15, the RMS
	/// narrative precedent). A value carrying an envelope prefix or the redaction placeholder is dropped regardless.
	/// Enrollment bumps the generation key, and the rebuild that follows re-projects without those columns.
	/// </summary>
	public class SearchProjectionService : ISearchProjectionService
	{
		private const int TitleMax = 400;
		private const int SummaryMax = 1000;
		private const int KeywordsMax = 400;
		private const int SearchTextMax = 8000;

		private static readonly Regex HtmlTags = new Regex("<[^>]+>", RegexOptions.Compiled);
		private static readonly Regex Whitespace = new Regex("\\s+", RegexOptions.Compiled);

		private readonly ISearchProjectionsRepository _projections;
		private readonly IDepartmentDataProtectionService _dataProtection;
		private readonly IDeploymentPersonnelRepository _deploymentPersonnel;

		/// <param name="deploymentPersonnel">Roster rows for the deployment projection's participants; optional only for hosts without the Business Operations repositories.</param>
		public SearchProjectionService(ISearchProjectionsRepository projections, IDepartmentDataProtectionService dataProtection,
			IDeploymentPersonnelRepository deploymentPersonnel = null)
		{
			_projections = projections ?? throw new ArgumentNullException(nameof(projections));
			_dataProtection = dataProtection ?? throw new ArgumentNullException(nameof(dataProtection));
			_deploymentPersonnel = deploymentPersonnel;
		}

		// ---- hooks -----------------------------------------------------------------------------------------------

		public Task ProjectCallAsync(Call call, CancellationToken cancellationToken = default)
			=> Guarded(SearchEntityTypes.Call, call?.DepartmentId ?? 0, call?.CallId.ToString(), call != null && call.IsDeleted, () => BuildCallAsync(call), cancellationToken);

		public Task ProjectUnitAsync(Unit unit, CancellationToken cancellationToken = default)
			=> Guarded(SearchEntityTypes.Unit, unit?.DepartmentId ?? 0, unit?.UnitId.ToString(), false, () => BuildUnitAsync(unit), cancellationToken);

		public Task ProjectPersonnelAsync(int departmentId, UserProfile profile, int? groupId, bool? isActive, CancellationToken cancellationToken = default)
			=> Guarded(SearchEntityTypes.Personnel, departmentId, profile?.UserId, false, async () =>
			{
				if (!groupId.HasValue || !isActive.HasValue)
				{
					var existing = await _projections.GetAsync(departmentId, SearchEntityTypes.Personnel, profile.UserId);
					groupId = groupId ?? existing?.GroupId;
					isActive = isActive ?? existing?.IsActive ?? true;
				}
				return await BuildPersonnelAsync(departmentId, profile, groupId, isActive.Value);
			}, cancellationToken);

		public Task ProjectContactAsync(Contact contact, CancellationToken cancellationToken = default)
			=> Guarded(SearchEntityTypes.Contact, contact?.DepartmentId ?? 0, contact?.ContactId, contact != null && contact.IsDeleted, () => BuildContactAsync(contact), cancellationToken);

		public Task ProjectMessageAsync(Message message, CancellationToken cancellationToken = default)
			=> Guarded(SearchEntityTypes.Message, message?.DepartmentId ?? 0, message?.MessageId.ToString(), message != null && message.IsDeleted, () => BuildMessageAsync(message), cancellationToken);

		public Task ProjectDocumentAsync(Document document, CancellationToken cancellationToken = default)
			=> Guarded(SearchEntityTypes.Document, document?.DepartmentId ?? 0, document?.DocumentId.ToString(), false, () => BuildDocumentAsync(document), cancellationToken);

		public Task ProjectNoteAsync(Note note, CancellationToken cancellationToken = default)
			=> Guarded(SearchEntityTypes.Note, note?.DepartmentId ?? 0, note?.NoteId.ToString(), false, () => BuildNoteAsync(note), cancellationToken);

		public async Task RemoveAsync(int departmentId, string entityType, string entityId, CancellationToken cancellationToken = default)
		{
			if (departmentId <= 0 || string.IsNullOrWhiteSpace(entityType) || string.IsNullOrWhiteSpace(entityId))
				return;
			try { await _projections.SoftDeleteAsync(departmentId, entityType, entityId, cancellationToken); }
			catch (Exception ex) { Logging.LogException(ex, $"Search projection removal failed for {entityType} {entityId} in department {departmentId}."); }
		}

		public async Task<SearchProjection> UpsertAsync(SearchProjection projection, CancellationToken cancellationToken = default)
		{
			if (projection == null)
				return null;
			return await _projections.UpsertAsync(projection, cancellationToken);
		}

		private async Task Guarded(string entityType, int departmentId, string entityId, bool deleted, Func<Task<SearchProjection>> build, CancellationToken cancellationToken)
		{
			try
			{
				if (departmentId <= 0 || string.IsNullOrWhiteSpace(entityId) || entityId == "0")
					return;
				if (deleted)
				{
					await _projections.SoftDeleteAsync(departmentId, entityType, entityId, cancellationToken);
					return;
				}
				var projection = await build();
				if (projection == null)
					await _projections.SoftDeleteAsync(departmentId, entityType, entityId, cancellationToken);
				else
					await _projections.UpsertAsync(projection, cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				// Never fail the entity write over the search projection; the next rebuild reconciles.
				Logging.LogException(ex, $"Search projection failed for {entityType} {entityId} in department {departmentId}.");
			}
		}

		public Task ProjectInvoiceAsync(Invoice invoice, CancellationToken cancellationToken = default)
			=> Guarded(SearchEntityTypes.Invoice, invoice?.DepartmentId ?? 0, invoice?.InvoiceId, invoice != null && invoice.IsDeleted, () => BuildInvoiceAsync(invoice), cancellationToken);

		public Task ProjectRateCardAsync(RateCard rateCard, CancellationToken cancellationToken = default)
			=> Guarded(SearchEntityTypes.RateCard, rateCard?.DepartmentId ?? 0, rateCard?.RateCardId, rateCard != null && rateCard.IsDeleted, () => BuildRateCardAsync(rateCard), cancellationToken);

		public Task ProjectBidAsync(Bid bid, CancellationToken cancellationToken = default)
			=> Guarded(SearchEntityTypes.Bid, bid?.DepartmentId ?? 0, bid?.BidId, bid != null && bid.IsDeleted, () => BuildBidAsync(bid), cancellationToken);

		public Task ProjectServiceContractAsync(ServiceContract contract, CancellationToken cancellationToken = default)
			=> Guarded(SearchEntityTypes.ServiceContract, contract?.DepartmentId ?? 0, contract?.ServiceContractId, contract != null && contract.IsDeleted, () => BuildServiceContractAsync(contract), cancellationToken);

		public Task ProjectDeploymentAsync(Deployment deployment, CancellationToken cancellationToken = default)
			=> Guarded(SearchEntityTypes.Deployment, deployment?.DepartmentId ?? 0, deployment?.DeploymentId, deployment != null && deployment.IsDeleted, () => BuildDeploymentAsync(deployment), cancellationToken);

		public Task ProjectCertificationTypeAsync(DepartmentCertificationType type, CancellationToken cancellationToken = default)
			=> Guarded(SearchEntityTypes.CertificationType, type?.DepartmentId ?? 0, type?.DepartmentCertificationTypeId.ToString(), type != null && type.IsDeleted, () => BuildCertificationTypeAsync(type), cancellationToken);

		// ---- builders --------------------------------------------------------------------------------------------

		public async Task<SearchProjection> BuildCallAsync(Call call)
		{
			if (call == null || call.DepartmentId <= 0 || call.CallId <= 0 || call.IsDeleted)
				return null;

			var ctx = await ContextAsync(call.DepartmentId);
			var number = Safe(call.Number);
			var name = ctx.ProtectedTextAllowed ? Safe(call.Name) : null;
			var nature = ctx.ProtectedTextAllowed ? Safe(call.NatureOfCall) : null;
			var type = ctx.ProtectedTextAllowed ? Safe(call.Type) : null;
			var address = ctx.ProtectedTextAllowed ? Safe(call.Address) : null;
			var incident = ctx.ProtectedTextAllowed ? Safe(call.IncidentNumber) : null;
			var reference = ctx.ProtectedTextAllowed ? Safe(call.ReferenceNumber) : null;
			var external = ctx.ProtectedTextAllowed ? Safe(call.ExternalIdentifier) : null;
			var state = Enum.IsDefined(typeof(CallStates), call.State) ? ((CallStates)call.State).ToString() : call.State.ToString();

			var p = New(call.DepartmentId, SearchEntityTypes.Call, call.CallId.ToString(), ctx);
			p.Title = Cap(name ?? (number != null ? "Call " + number : "Call " + call.CallId), TitleMax);
			p.Summary = Cap(Join(" · ", nature, type), SummaryMax);
			p.Keywords = Cap(Join(" ", number, incident, reference, external), KeywordsMax);
			p.SearchText = Cap(Join(" ", address, nature, type), SearchTextMax);
			p.Category = type;
			p.Status = state;
			p.Priority = call.Priority;
			p.IsActive = call.State == (int)CallStates.Active;
			p.OccurredOn = call.LoggedOn == default ? DateTime.UtcNow : call.LoggedOn;
			p.OwnerUserId = Safe(call.ReportingUserId);
			p.Url = $"/User/Dispatch/ViewCall?callId={call.CallId}";
			p.MetadataJson = Json(new Dictionary<string, string>
			{
				["Number"] = number,
				["Priority"] = call.Priority.ToString(),
				["State"] = state,
				["LoggedOn"] = p.OccurredOn.ToString("o"),
				["IncidentNumber"] = incident
			});
			return p;
		}

		public async Task<SearchProjection> BuildUnitAsync(Unit unit)
		{
			if (unit == null || unit.DepartmentId <= 0 || unit.UnitId <= 0)
				return null;

			var ctx = await ContextAsync(unit.DepartmentId);
			var name = Safe(unit.Name);
			if (name == null)
				return null;

			var p = New(unit.DepartmentId, SearchEntityTypes.Unit, unit.UnitId.ToString(), ctx);
			p.Title = Cap(name, TitleMax);
			p.Summary = Cap(Safe(unit.Type), SummaryMax);
			p.Keywords = Cap(Join(" ", name, Safe(unit.VIN), Safe(unit.PlateNumber)), KeywordsMax);
			p.Category = Safe(unit.Type);
			p.GroupId = unit.StationGroupId;
			p.IsActive = true;
			p.OccurredOn = DateTime.UtcNow;
			p.Url = "/User/Units";
			p.MetadataJson = Json(new Dictionary<string, string> { ["Type"] = Safe(unit.Type), ["StationGroupId"] = unit.StationGroupId?.ToString() });
			return p;
		}

		public async Task<SearchProjection> BuildPersonnelAsync(int departmentId, UserProfile profile, int? groupId, bool isActive)
		{
			if (profile == null || departmentId <= 0 || string.IsNullOrWhiteSpace(profile.UserId))
				return null;

			var ctx = await ContextAsync(departmentId);
			var first = Safe(profile.FirstName);
			var last = Safe(profile.LastName);
			var name = Join(" ", first, last);
			if (name == null)
				return null;

			// Member identification numbers are cataloged (DepartmentMemberSensitiveData); the legacy profile column
			// is treated the same way. Phones, e-mail and addresses are never projected (plan R3).
			var idNumber = ctx.ProtectedTextAllowed ? Safe(profile.IdentificationNumber) : null;

			var p = New(departmentId, SearchEntityTypes.Personnel, profile.UserId, ctx);
			p.Title = Cap(name, TitleMax);
			p.Keywords = Cap(Join(" ", idNumber, first, last), KeywordsMax);
			p.GroupId = groupId;
			p.OwnerUserId = profile.UserId;
			p.IsActive = isActive;
			p.OccurredOn = profile.LastUpdated ?? DateTime.UtcNow;
			p.Url = $"/User/Personnel/ViewPerson?userId={Uri.EscapeDataString(profile.UserId)}";
			p.MetadataJson = Json(new Dictionary<string, string> { ["IdentificationNumber"] = idNumber, ["GroupId"] = groupId?.ToString(), ["IsActive"] = isActive ? "true" : "false" });
			return p;
		}

		public async Task<SearchProjection> BuildContactAsync(Contact contact)
		{
			if (contact == null || contact.DepartmentId <= 0 || string.IsNullOrWhiteSpace(contact.ContactId) || contact.IsDeleted)
				return null;

			var ctx = await ContextAsync(contact.DepartmentId);
			// Every Contact column is cataloged: in an enforced department nothing textual can be indexed.
			if (!ctx.ProtectedTextAllowed)
				return null;

			var first = Safe(contact.FirstName);
			var last = Safe(contact.LastName);
			var company = Safe(contact.CompanyName);
			var other = Safe(contact.OtherName);
			var title = Join(" ", first, last) ?? company ?? other;
			if (title == null)
				return null;

			var p = New(contact.DepartmentId, SearchEntityTypes.Contact, contact.ContactId, ctx);
			p.Title = Cap(title, TitleMax);
			p.Summary = Cap(Join(" · ", company != null && title != company ? company : null, Safe(contact.Description)), SummaryMax);
			p.Keywords = Cap(Join(" ", Safe(contact.Email), Digits(contact.CellPhoneNumber), Digits(contact.HomePhoneNumber), Digits(contact.OfficePhoneNumber)), KeywordsMax);
			p.SearchText = Cap(Join(" ", other, company, Safe(contact.Email), Safe(contact.OtherInfo), Safe(contact.Website)), SearchTextMax);
			p.Category = contact.ContactType == 1 ? "Company" : "Person";
			p.IsActive = true;
			p.OccurredOn = DateTime.UtcNow;
			p.Url = $"/User/Contacts/View?contactId={Uri.EscapeDataString(contact.ContactId)}";
			p.MetadataJson = Json(new Dictionary<string, string> { ["ContactType"] = contact.ContactType.ToString(), ["CategoryId"] = Safe(contact.ContactCategoryId) });
			return p;
		}

		public async Task<SearchProjection> BuildMessageAsync(Message message)
		{
			if (message == null || !message.DepartmentId.HasValue || message.DepartmentId.Value <= 0 || message.MessageId <= 0 || message.IsDeleted)
				return null;

			var ctx = await ContextAsync(message.DepartmentId.Value);
			var subject = ctx.ProtectedTextAllowed ? Safe(message.Subject) : null;
			var body = ctx.ProtectedTextAllowed ? Safe(Strip(message.Body)) : null;

			var recipients = new List<string>();
			if (!string.IsNullOrWhiteSpace(message.ReceivingUserId))
				recipients.Add(message.ReceivingUserId);
			if (message.MessageRecipients != null)
				recipients.AddRange(message.MessageRecipients.Where(r => r != null && !r.IsDeleted && !string.IsNullOrWhiteSpace(r.UserId)).Select(r => r.UserId));

			var p = New(message.DepartmentId.Value, SearchEntityTypes.Message, message.MessageId.ToString(), ctx);
			p.Title = Cap(subject ?? "Message", TitleMax);
			p.Summary = Cap(body == null ? null : body.Substring(0, Math.Min(body.Length, 200)), SummaryMax);
			p.SearchText = Cap(body, SearchTextMax);
			p.Category = message.Type.ToString();
			p.OwnerUserId = Safe(message.SendingUserId);
			p.ParticipantUserIds = recipients.Count == 0 ? null : string.Join(",", recipients.Distinct());
			p.IsActive = !message.ExpireOn.HasValue || message.ExpireOn.Value > DateTime.UtcNow;
			p.OccurredOn = message.SentOn == default ? DateTime.UtcNow : message.SentOn;
			p.Url = $"/User/Messages/ViewMessage?messageId={message.MessageId}";
			p.MetadataJson = Json(new Dictionary<string, string> { ["Type"] = message.Type.ToString(), ["SentOn"] = p.OccurredOn.ToString("o"), ["IsBroadcast"] = message.IsBroadcast ? "true" : "false" });
			return p;
		}

		public async Task<SearchProjection> BuildDocumentAsync(Document document)
		{
			if (document == null || document.DepartmentId <= 0 || document.DocumentId <= 0)
				return null;

			var ctx = await ContextAsync(document.DepartmentId);
			var name = ctx.ProtectedTextAllowed ? Safe(document.Name) : null;
			var description = ctx.ProtectedTextAllowed ? Safe(document.Description) : null;
			var filename = ctx.ProtectedTextAllowed ? Safe(document.Filename) : null;

			var p = New(document.DepartmentId, SearchEntityTypes.Document, document.DocumentId.ToString(), ctx);
			p.Title = Cap(name ?? "Document " + document.DocumentId, TitleMax);
			p.Summary = Cap(description, SummaryMax);
			p.Keywords = Cap(filename, KeywordsMax);
			p.SearchText = Cap(Join(" ", filename, Safe(document.Category), Safe(document.Type)), SearchTextMax);
			p.Category = Safe(document.Category);
			p.IsAdminOnly = document.AdminsOnly;
			p.OwnerUserId = Safe(document.UserId);
			p.IsActive = !document.RemoveOn.HasValue || document.RemoveOn.Value > DateTime.UtcNow;
			p.OccurredOn = document.AddedOn == default ? DateTime.UtcNow : document.AddedOn;
			p.Url = $"/User/Documents/ViewDocument?documentId={document.DocumentId}";
			p.MetadataJson = Json(new Dictionary<string, string> { ["Category"] = Safe(document.Category), ["Type"] = Safe(document.Type), ["AddedOn"] = p.OccurredOn.ToString("o") });
			return p;
		}

		public async Task<SearchProjection> BuildNoteAsync(Note note)
		{
			if (note == null || note.DepartmentId <= 0 || note.NoteId <= 0)
				return null;

			// Notes are not in the protected catalog; title and body are department-authored plain text.
			var ctx = await ContextAsync(note.DepartmentId);
			var title = Safe(note.Title);
			var body = Safe(Strip(note.Body));
			if (title == null && body == null)
				return null;

			var p = New(note.DepartmentId, SearchEntityTypes.Note, note.NoteId.ToString(), ctx);
			p.Title = Cap(title ?? "Note " + note.NoteId, TitleMax);
			p.Summary = Cap(body == null ? null : body.Substring(0, Math.Min(body.Length, 200)), SummaryMax);
			p.SearchText = Cap(body, SearchTextMax);
			p.Category = Safe(note.Category);
			p.IsAdminOnly = note.IsAdminOnly;
			p.OwnerUserId = Safe(note.UserId);
			p.IsActive = !note.ExpiresOn.HasValue || note.ExpiresOn.Value > DateTime.UtcNow;
			p.OccurredOn = note.AddedOn == default ? DateTime.UtcNow : note.AddedOn;
			p.Url = $"/User/Notes/View?noteId={note.NoteId}";
			p.MetadataJson = Json(new Dictionary<string, string> { ["Category"] = Safe(note.Category), ["Color"] = Safe(note.Color), ["AddedOn"] = p.OccurredOn.ToString("o") });
			return p;
		}

		// ---- Workforce & Business Operations families (decision 41): identifier, title, status. Never an amount, a line, a note, an e-mail or a person. ----

		public async Task<SearchProjection> BuildInvoiceAsync(Invoice invoice)
		{
			if (invoice == null || invoice.DepartmentId <= 0 || string.IsNullOrWhiteSpace(invoice.InvoiceId) || invoice.IsDeleted)
				return null;
			var ctx = await ContextAsync(invoice.DepartmentId);
			var status = Enum.IsDefined(typeof(InvoiceStatus), invoice.Status) ? ((InvoiceStatus)invoice.Status).ToString() : invoice.Status.ToString();
			var p = New(invoice.DepartmentId, SearchEntityTypes.Invoice, invoice.InvoiceId, ctx);
			p.Title = Cap("Invoice #" + invoice.InvoiceNumber, TitleMax);
			p.Summary = Cap(Join(" · ", status, invoice.IssuedOn?.ToString("yyyy-MM-dd"), Safe(invoice.Currency)), SummaryMax);
			p.Keywords = Cap(Join(" ", invoice.InvoiceNumber.ToString(), Safe(invoice.DeploymentId), Safe(invoice.ServiceContractId)), KeywordsMax);
			p.Category = status;
			p.IsActive = invoice.Status != (int)InvoiceStatus.Void;
			p.OccurredOn = invoice.IssuedOn ?? (invoice.AddedOn == default ? DateTime.UtcNow : invoice.AddedOn);
			p.Url = $"/User/Invoicing/View?id={Uri.EscapeDataString(invoice.InvoiceId)}";
			p.MetadataJson = Json(new Dictionary<string, string> { ["Status"] = status, ["ContactId"] = Safe(invoice.ContactId), ["DeploymentId"] = Safe(invoice.DeploymentId) });
			return p;
		}

		public async Task<SearchProjection> BuildRateCardAsync(RateCard rateCard)
		{
			if (rateCard == null || rateCard.DepartmentId <= 0 || string.IsNullOrWhiteSpace(rateCard.RateCardId) || rateCard.IsDeleted)
				return null;
			var ctx = await ContextAsync(rateCard.DepartmentId);
			var name = Safe(rateCard.Name);
			if (name == null) return null;
			var p = New(rateCard.DepartmentId, SearchEntityTypes.RateCard, rateCard.RateCardId, ctx);
			p.Title = Cap(name, TitleMax);
			p.Summary = Cap(Join(" · ", rateCard.IsDefault ? "Default" : null, rateCard.Active ? "Active" : "Inactive", Safe(rateCard.Description)), SummaryMax);
			p.Category = rateCard.Active ? "Active" : "Inactive";
			p.IsActive = rateCard.Active;
			p.OccurredOn = rateCard.AddedOn == default ? DateTime.UtcNow : rateCard.AddedOn;
			p.Url = $"/User/Invoicing/EditRateCard?id={Uri.EscapeDataString(rateCard.RateCardId)}";
			p.MetadataJson = Json(new Dictionary<string, string> { ["IsDefault"] = rateCard.IsDefault.ToString() });
			return p;
		}

		public async Task<SearchProjection> BuildBidAsync(Bid bid)
		{
			if (bid == null || bid.DepartmentId <= 0 || string.IsNullOrWhiteSpace(bid.BidId) || bid.IsDeleted)
				return null;
			var ctx = await ContextAsync(bid.DepartmentId);
			var status = Enum.IsDefined(typeof(BidStatuses), bid.Status) ? ((BidStatuses)bid.Status).ToString() : bid.Status.ToString();
			var p = New(bid.DepartmentId, SearchEntityTypes.Bid, bid.BidId, ctx);
			p.Title = Cap(Join(" ", "Bid #" + bid.BidNumber, Safe(bid.Title)), TitleMax);
			p.Summary = Cap(Join(" · ", status, Safe(bid.IncidentNumber), bid.RequestedStartOn?.ToString("yyyy-MM-dd")), SummaryMax);
			p.Keywords = Cap(Join(" ", bid.BidNumber.ToString(), Safe(bid.IncidentNumber), Safe(bid.ConvertedDeploymentId)), KeywordsMax);
			p.Category = status;
			p.IsActive = bid.Status != (int)BidStatuses.Declined && bid.Status != (int)BidStatuses.Expired;
			p.OccurredOn = bid.AddedOn == default ? DateTime.UtcNow : bid.AddedOn;
			p.Url = $"/User/Bids/View?id={Uri.EscapeDataString(bid.BidId)}";
			p.MetadataJson = Json(new Dictionary<string, string> { ["Status"] = status, ["ContactId"] = Safe(bid.ContactId), ["DeploymentId"] = Safe(bid.ConvertedDeploymentId) });
			return p;
		}

		public async Task<SearchProjection> BuildServiceContractAsync(ServiceContract contract)
		{
			if (contract == null || contract.DepartmentId <= 0 || string.IsNullOrWhiteSpace(contract.ServiceContractId) || contract.IsDeleted)
				return null;
			var ctx = await ContextAsync(contract.DepartmentId);
			var name = Safe(contract.Name);
			if (name == null) return null;
			var status = Enum.IsDefined(typeof(ServiceContractStatuses), contract.Status) ? ((ServiceContractStatuses)contract.Status).ToString() : contract.Status.ToString();
			var p = New(contract.DepartmentId, SearchEntityTypes.ServiceContract, contract.ServiceContractId, ctx);
			p.Title = Cap(Join(" ", Safe(contract.ContractNumber), name), TitleMax);
			p.Summary = Cap(Join(" · ", status, contract.StartOn.ToString("yyyy-MM-dd") + " – " + (contract.EndOn?.ToString("yyyy-MM-dd") ?? "…")), SummaryMax);
			p.Keywords = Cap(Safe(contract.ContractNumber), KeywordsMax);
			p.Category = status;
			p.IsActive = contract.Status == (int)ServiceContractStatuses.Active;
			p.OccurredOn = contract.StartOn == default ? DateTime.UtcNow : contract.StartOn;
			p.Url = $"/User/Contracts/View?id={Uri.EscapeDataString(contract.ServiceContractId)}";
			p.MetadataJson = Json(new Dictionary<string, string> { ["Status"] = status, ["ContactId"] = Safe(contract.ContactId) });
			return p;
		}

		public async Task<SearchProjection> BuildDeploymentAsync(Deployment deployment)
		{
			if (deployment == null || deployment.DepartmentId <= 0 || string.IsNullOrWhiteSpace(deployment.DeploymentId) || deployment.IsDeleted)
				return null;
			var ctx = await ContextAsync(deployment.DepartmentId);
			var name = Safe(deployment.Name);
			if (name == null) return null;
			var status = Enum.IsDefined(typeof(DeploymentStatuses), deployment.Status) ? ((DeploymentStatuses)deployment.Status).ToString() : deployment.Status.ToString();
			// Deployments.Notes is ADP catalog 27 and never indexed; the order / request / incident identifiers print on every claim and are plain.
			var p = New(deployment.DepartmentId, SearchEntityTypes.Deployment, deployment.DeploymentId, ctx);
			p.Title = Cap(name, TitleMax);
			p.Summary = Cap(Join(" · ", status, Safe(deployment.IncidentNumber), Safe(deployment.ResourceOrderNumber), deployment.StartOn?.ToString("yyyy-MM-dd")), SummaryMax);
			p.Keywords = Cap(Join(" ", Safe(deployment.IncidentNumber), Safe(deployment.ResourceOrderNumber), Safe(deployment.RequestNumber), Safe(deployment.CostCode), deployment.CallId?.ToString()), KeywordsMax);
			p.Category = status;
			p.IsActive = deployment.IsOpen;
			p.OccurredOn = deployment.StartOn ?? (deployment.AddedOn == default ? DateTime.UtcNow : deployment.AddedOn);
			p.Url = $"/User/Deployments/View?id={Uri.EscapeDataString(deployment.DeploymentId)}";
			p.MetadataJson = Json(new Dictionary<string, string> { ["Status"] = status, ["FinanceMode"] = deployment.FinanceMode.ToString(), ["CallId"] = deployment.CallId?.ToString() });
			// The roster is the deployment's participant set: a member without Deployments/View reaches a deployment they are
			// rostered on (any personnel row, removed or not — the deployment page's rule), and the index applies that scope for
			// such a caller so unrostered deployments never enter their candidate window. Read here rather than from
			// deployment.Personnel, which is only populated on the aggregate read paths.
			p.ParticipantUserIds = await RosterAsync(deployment.DeploymentId);
			return p;
		}

		private async Task<string> RosterAsync(string deploymentId)
		{
			if (_deploymentPersonnel == null) return null;
			var users = (await _deploymentPersonnel.GetByDeploymentAsync(deploymentId) ?? Enumerable.Empty<DeploymentPersonnel>())
				.Select(r => r?.UserId).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToList();
			return users.Count == 0 ? null : string.Join(",", users);
		}

		public async Task<SearchProjection> BuildCertificationTypeAsync(DepartmentCertificationType type)
		{
			if (type == null || type.DepartmentId <= 0 || type.DepartmentCertificationTypeId <= 0 || type.IsDeleted)
				return null;
			var ctx = await ContextAsync(type.DepartmentId);
			var name = Safe(type.Type);
			if (name == null) return null;
			var p = New(type.DepartmentId, SearchEntityTypes.CertificationType, type.DepartmentCertificationTypeId.ToString(), ctx);
			p.Title = Cap(name, TitleMax);
			p.Summary = Cap(Join(" · ", Safe(type.Code), Safe(type.IssuingAuthority), Safe(type.Description)), SummaryMax);
			p.Keywords = Cap(Join(" ", Safe(type.Code), Safe(type.IssuingAuthority)), KeywordsMax);
			p.Category = type.IsActive ? "Active" : "Inactive";
			p.IsActive = type.IsActive;
			p.OccurredOn = DateTime.UtcNow;
			p.Url = $"/User/Certifications/EditType?id={type.DepartmentCertificationTypeId}";
			p.MetadataJson = Json(new Dictionary<string, string> { ["Code"] = Safe(type.Code), ["AppliesTo"] = type.AppliesTo.ToString() });
			return p;
		}

		// ---- helpers ---------------------------------------------------------------------------------------------

		private sealed class ProjectionContext
		{
			public bool ProtectedTextAllowed;
			public int CatalogVersion;
			public long PolicyEpoch;
		}

		private async Task<ProjectionContext> ContextAsync(int departmentId)
		{
			var ctx = new ProjectionContext();
			try
			{
				// Unknown protection state never widens exposure: index metadata only.
				ctx.ProtectedTextAllowed = !await _dataProtection.IsProtectionEnforcedAsync(departmentId);
			}
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Protection state for department {departmentId} could not be determined; projecting metadata only.");
				ctx.ProtectedTextAllowed = false;
			}
			try { ctx.CatalogVersion = await _dataProtection.GetPinnedCatalogVersionAsync(departmentId); } catch (Exception ex) { Logging.LogException(ex); }
			try { ctx.PolicyEpoch = (await _dataProtection.GetPolicyByDepartmentIdAsync(departmentId))?.PolicyEpoch ?? 0; } catch (Exception ex) { Logging.LogException(ex); }
			return ctx;
		}

		private static SearchProjection New(int departmentId, string entityType, string entityId, ProjectionContext ctx)
		{
			return new SearchProjection
			{
				DepartmentId = departmentId,
				EntityType = entityType,
				EntityId = entityId,
				ProtectedCatalogVersion = ctx.CatalogVersion,
				PolicyEpoch = ctx.PolicyEpoch,
				IncludesProtectedText = ctx.ProtectedTextAllowed,
				IsActive = true,
				OccurredOn = DateTime.UtcNow
			};
		}

		/// <summary>Trimmed value, or null when empty, enveloped or redacted.</summary>
		public static string Safe(string value)
		{
			if (string.IsNullOrWhiteSpace(value))
				return null;
			var trimmed = value.Trim();
			if (ProtectedDataEnvelope.HasEnvelopePrefix(trimmed) || trimmed == ProtectedDataEnvelope.RedactionValue)
				return null;
			return trimmed;
		}

		private static string Digits(string value)
		{
			var safe = Safe(value);
			if (safe == null)
				return null;
			var digits = new string(safe.Where(char.IsDigit).ToArray());
			return digits.Length >= 4 ? digits : null;
		}

		private static string Strip(string html)
		{
			if (string.IsNullOrWhiteSpace(html))
				return null;
			var text = HtmlTags.Replace(html, " ");
			text = System.Net.WebUtility.HtmlDecode(text);
			return Whitespace.Replace(text, " ").Trim();
		}

		private static string Join(string separator, params string[] parts)
		{
			var kept = parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			return kept.Count == 0 ? null : string.Join(separator, kept);
		}

		private static string Cap(string value, int max)
		{
			if (string.IsNullOrEmpty(value))
				return null;
			return value.Length <= max ? value : value.Substring(0, max);
		}

		private static string Json(Dictionary<string, string> values)
		{
			var kept = values.Where(kv => !string.IsNullOrWhiteSpace(kv.Value)).ToDictionary(kv => kv.Key, kv => kv.Value);
			return kept.Count == 0 ? null : JsonConvert.SerializeObject(kept);
		}
	}
}
