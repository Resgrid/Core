using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Invoicing;
using Resgrid.Model.Repositories;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;

namespace Resgrid.Services.Invoicing
{
	/// <summary>
	/// Service contracts and department compliance documents (Workforce &amp; Business Operations plan, C4;
	/// decisions 14, 22, 24). Status changes and the expiry sweep publish through the domain outbox under the
	/// <see cref="ContractorWorkflowPayload.Producer"/> producer. Nothing here is under Advanced Data Protection:
	/// contracts, submission addresses and compliance documents are customer-facing (they ride the invoice packet)
	/// and must read whole for people outside the department. Callers authorize.
	/// </summary>
	public class ServiceContractService : IServiceContractService
	{
		/// <summary>Days before <c>EndOn</c> the sweep starts announcing a contract (once per calendar day).</summary>
		public const int ExpiryLeadDays = 30;
		private static readonly HashSet<int> ExpiringAnnounced = new HashSet<int>();

		private readonly IServiceContractRepository _contracts;
		private readonly IServiceContractDocumentRequirementRepository _requirements;
		private readonly IDepartmentComplianceDocumentRepository _documents;
		private readonly IDeploymentRepository _deployments;
		private readonly IDeploymentAttachmentRepository _attachments;
		private readonly ICustomerBillingProfileRepository _profiles;
		private readonly IContactsService _contactsService;
		private readonly IDepartmentsService _departmentsService;
		private readonly IDomainEventOutboxService _outbox;
		private readonly IEventAggregator _eventAggregator;
		private readonly IUnitOfWork _unitOfWork;
		private readonly Lazy<ICommunicationService> _communication;
		private readonly Lazy<IDepartmentSettingsService> _departmentSettings;
		private readonly Lazy<ISearchProjectionService> _searchProjections;

		public ServiceContractService(IServiceContractRepository contracts, IServiceContractDocumentRequirementRepository requirements, IDepartmentComplianceDocumentRepository documents,
			IDeploymentRepository deployments, IDeploymentAttachmentRepository attachments, ICustomerBillingProfileRepository profiles, IContactsService contactsService,
			IDepartmentsService departmentsService, IDomainEventOutboxService outbox, IEventAggregator eventAggregator, IUnitOfWork unitOfWork,
			Lazy<ICommunicationService> communication = null, Lazy<IDepartmentSettingsService> departmentSettings = null, Lazy<ISearchProjectionService> searchProjections = null)
		{
			_contracts = contracts;
			_requirements = requirements;
			_documents = documents;
			_deployments = deployments;
			_attachments = attachments;
			_profiles = profiles;
			_contactsService = contactsService;
			_departmentsService = departmentsService;
			_outbox = outbox;
			_eventAggregator = eventAggregator;
			_unitOfWork = unitOfWork;
			_communication = communication;
			_departmentSettings = departmentSettings;
			_searchProjections = searchProjections;
		}

		#region Contracts

		public async Task<List<ServiceContract>> GetContractsForDepartmentAsync(int departmentId, ServiceContractStatuses? status = null)
		{
			return (await _contracts.GetForDepartmentAsync(departmentId, status.HasValue ? (int?)status.Value : null))?.ToList() ?? new List<ServiceContract>();
		}

		public async Task<List<ServiceContract>> GetContractsByContactIdAsync(string contactId, int departmentId)
		{
			if (string.IsNullOrWhiteSpace(contactId)) return new List<ServiceContract>();
			return (await _contracts.GetByContactIdAsync(departmentId, contactId))?.ToList() ?? new List<ServiceContract>();
		}

		public async Task<ServiceContract> GetContractByIdAsync(string serviceContractId, int departmentId)
		{
			if (string.IsNullOrWhiteSpace(serviceContractId)) return null;
			var contract = await _contracts.GetByIdForDepartmentAsync(serviceContractId, departmentId);
			if (contract == null || contract.IsDeleted) return null;
			contract.Requirements = (await _requirements.GetByContractAsync(serviceContractId))?.ToList() ?? new List<ServiceContractDocumentRequirement>();
			return contract;
		}

		public async Task<ServiceContract> SaveContractAsync(ServiceContract contract, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			if (contract == null) throw new ArgumentNullException(nameof(contract));
			if (string.IsNullOrWhiteSpace(contract.Name)) throw new InvalidOperationException("contracts_name_required");
			if (string.IsNullOrWhiteSpace(contract.ContactId)) throw new InvalidOperationException("contracts_contact_required");
			if (contract.EndOn.HasValue && contract.EndOn.Value < contract.StartOn) throw new InvalidOperationException("contracts_dates_invalid");
			if (contract.DiscountPercent.HasValue && (contract.DiscountPercent < 0 || contract.DiscountPercent > 100)) throw new InvalidOperationException("contracts_discount_invalid");
			if (!Enum.IsDefined(typeof(ServiceContractTypes), contract.ContractType)) throw new InvalidOperationException("contracts_type_invalid");
			var contact = await _contactsService.GetContactByIdAsync(contract.ContactId);
			if (contact == null || contact.DepartmentId != contract.DepartmentId || contact.IsDeleted) throw new InvalidOperationException("contracts_contact_not_found");
			if (string.IsNullOrWhiteSpace(contract.CustomerBillingProfileId))
				contract.CustomerBillingProfileId = (await _profiles.GetByContactIdAsync(contract.ContactId, contract.DepartmentId))?.CustomerBillingProfileId;

			var now = DateTime.UtcNow;
			var existing = string.IsNullOrWhiteSpace(contract.ServiceContractId) ? null : await _contracts.GetByIdForDepartmentAsync(contract.ServiceContractId, contract.DepartmentId);
			if (existing != null && existing.IsDeleted) throw new InvalidOperationException("contracts_not_found");
			var before = existing == null ? null : Snapshot(existing);

			var target = existing ?? new ServiceContract { DepartmentId = contract.DepartmentId, Status = (int)ServiceContractStatuses.Draft, AddedOn = now, AddedByUserId = userId };
			target.ContactId = contract.ContactId;
			target.CustomerBillingProfileId = contract.CustomerBillingProfileId;
			target.ContractNumber = Trim(contract.ContractNumber);
			target.Name = contract.Name.Trim();
			target.ContractType = contract.ContractType;
			target.StartOn = contract.StartOn;
			target.EndOn = contract.EndOn;
			target.RateScheduleId = Trim(contract.RateScheduleId);
			target.DiscountPercent = contract.DiscountPercent;
			target.TermsNetDays = contract.TermsNetDays;
			target.InvoiceSubmissionEmail = Trim(contract.InvoiceSubmissionEmail);
			target.MaxDeploymentDays = contract.MaxDeploymentDays;
			target.ResponseTimeMinutes = contract.ResponseTimeMinutes;
			target.PointOfHire = Trim(contract.PointOfHire);
			target.DocumentTemplateKey = Trim(contract.DocumentTemplateKey);
			target.Notes = Trim(contract.Notes);
			if (existing == null && contract.Status == (int)ServiceContractStatuses.Active) target.Status = (int)ServiceContractStatuses.Active;
			if (existing != null) { target.EditedOn = now; target.EditedByUserId = userId; }

			var saved = await _contracts.SaveOrUpdateAsync(target, cancellationToken);
			if (_searchProjections?.Value != null) await _searchProjections.Value.ProjectServiceContractAsync(target, cancellationToken);
			Audit(contract.DepartmentId, userId, existing == null ? AuditLogTypes.ServiceContractCreated : AuditLogTypes.ServiceContractUpdated, ipAddress, userAgent, before, saved);
			if (existing == null && saved.Status == (int)ServiceContractStatuses.Active)
				await PublishAsync(saved, WorkflowTriggerEventType.ContractStatusChanged, (int)ServiceContractStatuses.Draft, cancellationToken);
			return await GetContractByIdAsync(saved.ServiceContractId, contract.DepartmentId);
		}

		public static bool IsValidTransition(ServiceContractStatuses from, ServiceContractStatuses to) => (from, to) switch
		{
			(ServiceContractStatuses.Draft, ServiceContractStatuses.Active) => true,
			(ServiceContractStatuses.Draft, ServiceContractStatuses.Terminated) => true,
			(ServiceContractStatuses.Active, ServiceContractStatuses.Suspended) => true,
			(ServiceContractStatuses.Active, ServiceContractStatuses.Expired) => true,
			(ServiceContractStatuses.Active, ServiceContractStatuses.Terminated) => true,
			(ServiceContractStatuses.Suspended, ServiceContractStatuses.Active) => true,
			(ServiceContractStatuses.Suspended, ServiceContractStatuses.Terminated) => true,
			(ServiceContractStatuses.Expired, ServiceContractStatuses.Active) => true,
			_ => false
		};

		public async Task<ServiceContract> SetContractStatusAsync(string serviceContractId, int departmentId, ServiceContractStatuses status, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var contract = await _contracts.GetByIdForDepartmentAsync(serviceContractId, departmentId);
			if (contract == null || contract.IsDeleted) throw new InvalidOperationException("contracts_not_found");
			var from = (ServiceContractStatuses)contract.Status;
			if (from == status) return await GetContractByIdAsync(serviceContractId, departmentId);
			if (!IsValidTransition(from, status)) throw new InvalidOperationException("contracts_status_transition_invalid");
			if (status == ServiceContractStatuses.Active && contract.EndOn.HasValue && contract.EndOn.Value < DateTime.UtcNow.Date) throw new InvalidOperationException("contracts_ended");

			var before = Snapshot(contract);
			contract.Status = (int)status;
			contract.EditedOn = DateTime.UtcNow;
			contract.EditedByUserId = userId;
			var saved = await _contracts.SaveOrUpdateAsync(contract, cancellationToken);
			if (_searchProjections?.Value != null) await _searchProjections.Value.ProjectServiceContractAsync(contract, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.ServiceContractStatusChanged, ipAddress, userAgent, before, saved);
			await PublishAsync(saved, WorkflowTriggerEventType.ContractStatusChanged, (int)from, cancellationToken);
			return await GetContractByIdAsync(serviceContractId, departmentId);
		}

		public async Task<bool> DeleteContractAsync(string serviceContractId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var contract = await _contracts.GetByIdForDepartmentAsync(serviceContractId, departmentId);
			if (contract == null || contract.IsDeleted) return false;
			if (contract.Status == (int)ServiceContractStatuses.Active) throw new InvalidOperationException("contracts_active");
			var before = Snapshot(contract);
			contract.IsDeleted = true;
			contract.EditedOn = DateTime.UtcNow;
			contract.EditedByUserId = userId;
			await _contracts.SaveOrUpdateAsync(contract, cancellationToken);
			if (_searchProjections?.Value != null) await _searchProjections.Value.ProjectServiceContractAsync(contract, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.ServiceContractDeleted, ipAddress, userAgent, before, contract);
			return true;
		}

		public async Task<List<ServiceContractDocumentRequirement>> SaveRequirementsAsync(string serviceContractId, int departmentId, List<ServiceContractDocumentRequirement> requirements, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var contract = await _contracts.GetByIdForDepartmentAsync(serviceContractId, departmentId);
			if (contract == null || contract.IsDeleted) throw new InvalidOperationException("contracts_not_found");
			var incoming = (requirements ?? new List<ServiceContractDocumentRequirement>()).Where(r => r != null && !string.IsNullOrWhiteSpace(r.Name)).ToList();
			if (incoming.Any(r => !Enum.IsDefined(typeof(DocumentRequirementStages), r.Stage) || (r.ComplianceDocumentType.HasValue && !Enum.IsDefined(typeof(ComplianceDocumentTypes), r.ComplianceDocumentType.Value))))
				throw new InvalidOperationException("contracts_requirement_invalid");

			var before = (await _requirements.GetByContractAsync(serviceContractId))?.ToList().CloneJsonToString();
			await _requirements.DeleteByContractAsync(serviceContractId, cancellationToken);
			var order = 0;
			var saved = new List<ServiceContractDocumentRequirement>();
			foreach (var requirement in incoming.OrderBy(r => r.SortOrder))
				saved.Add(await _requirements.SaveOrUpdateAsync(new ServiceContractDocumentRequirement
				{
					ServiceContractId = serviceContractId, Name = requirement.Name.Trim(), Stage = requirement.Stage, ComplianceDocumentType = requirement.ComplianceDocumentType, IsMandatory = requirement.IsMandatory, SortOrder = order++
				}, cancellationToken));
			contract.EditedOn = DateTime.UtcNow;
			contract.EditedByUserId = userId;
			await _contracts.SaveOrUpdateAsync(contract, cancellationToken);
			if (_searchProjections?.Value != null) await _searchProjections.Value.ProjectServiceContractAsync(contract, cancellationToken);
			var audit = DeploymentService.NewAuditEvent(departmentId, userId, AuditLogTypes.ServiceContractUpdated, ipAddress, userAgent);
			audit.Before = before;
			audit.After = saved.CloneJsonToString();
			_eventAggregator.SendMessage<AuditEvent>(audit);
			return saved;
		}

		#endregion

		#region Compliance documents

		public async Task<List<DepartmentComplianceDocument>> GetComplianceDocumentsAsync(int departmentId)
		{
			return (await _documents.GetForDepartmentAsync(departmentId))?.ToList() ?? new List<DepartmentComplianceDocument>();
		}

		public async Task<DepartmentComplianceDocument> GetComplianceDocumentAsync(int departmentComplianceDocumentId, int departmentId, bool includeData)
		{
			var document = await _documents.GetByIdWithDataAsync(departmentComplianceDocumentId);
			if (document == null || document.IsDeleted || document.DepartmentId != departmentId) return null;
			if (!includeData) document.Data = null;
			return document;
		}

		public async Task<DepartmentComplianceDocument> SaveComplianceDocumentAsync(DepartmentComplianceDocument document, byte[] data, string fileName, string contentType, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			if (document == null) throw new ArgumentNullException(nameof(document));
			if (string.IsNullOrWhiteSpace(document.Name)) throw new InvalidOperationException("compliance_name_required");
			if (!Enum.IsDefined(typeof(ComplianceDocumentTypes), document.DocumentType)) throw new InvalidOperationException("compliance_type_invalid");
			if (document.EffectiveOn.HasValue && document.ExpiresOn.HasValue && document.ExpiresOn < document.EffectiveOn) throw new InvalidOperationException("compliance_dates_invalid");
			if (data != null && data.Length > DeploymentService.MaxAttachmentBytes) throw new InvalidOperationException("compliance_file_too_large");

			var now = DateTime.UtcNow;
			var existing = document.DepartmentComplianceDocumentId > 0 ? await _documents.GetByIdWithDataAsync(document.DepartmentComplianceDocumentId) : null;
			if (existing != null && (existing.IsDeleted || existing.DepartmentId != document.DepartmentId)) throw new InvalidOperationException("compliance_not_found");
			var before = existing == null ? null : Snapshot(existing);

			var target = existing ?? new DepartmentComplianceDocument { DepartmentId = document.DepartmentId, AddedOn = now, AddedByUserId = userId };
			target.DocumentType = document.DocumentType;
			target.Name = document.Name.Trim();
			target.DocumentNumber = Trim(document.DocumentNumber);
			target.Issuer = Trim(document.Issuer);
			target.EffectiveOn = document.EffectiveOn;
			target.ExpiresOn = document.ExpiresOn;
			target.AlertLeadDays = Math.Clamp(document.AlertLeadDays, 0, 365);
			if (existing != null) { target.EditedOn = now; target.EditedByUserId = userId; }

			var replacingFile = data != null && data.Length > 0;
			if (replacingFile)
			{
				target.FileName = Trim(fileName);
				target.FileType = Trim(contentType);
				target.FileSize = data.Length;
				target.Data = data;
			}
			var saved = await _documents.SaveOrUpdateAsync(target, cancellationToken);
			Audit(document.DepartmentId, userId, existing == null ? AuditLogTypes.ComplianceDocumentAdded : AuditLogTypes.ComplianceDocumentUpdated, ipAddress, userAgent, before, saved);
			return await GetComplianceDocumentAsync(saved.DepartmentComplianceDocumentId, document.DepartmentId, includeData: false);
		}

		public async Task<bool> DeleteComplianceDocumentAsync(int departmentComplianceDocumentId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var document = await _documents.GetByIdWithDataAsync(departmentComplianceDocumentId);
			if (document == null || document.IsDeleted || document.DepartmentId != departmentId) return false;
			var before = Snapshot(document);
			document.IsDeleted = true;
			document.EditedOn = DateTime.UtcNow;
			document.EditedByUserId = userId;
			await _documents.SaveOrUpdateAsync(document, cancellationToken);
			Audit(departmentId, userId, AuditLogTypes.ComplianceDocumentRemoved, ipAddress, userAgent, before, document);
			return true;
		}

		#endregion

		#region Compliance evaluation

		public async Task<ContractComplianceResult> GetContractComplianceAsync(string deploymentId, int departmentId)
		{
			var deployment = await _deployments.GetByIdForDepartmentAsync(deploymentId, departmentId);
			if (deployment == null || deployment.IsDeleted) return null;
			var result = new ContractComplianceResult { DeploymentId = deploymentId, ServiceContractId = deployment.ServiceContractId };
			if (string.IsNullOrWhiteSpace(deployment.ServiceContractId)) return result;
			var attachments = (await _attachments.GetByDeploymentAsync(deploymentId))?.ToList() ?? new List<DeploymentAttachment>();
			await EvaluateAsync(result, deployment.ServiceContractId, departmentId, attachments, null);
			return result;
		}

		public async Task<ContractComplianceResult> GetContractComplianceForContractAsync(string serviceContractId, int departmentId)
		{
			var result = new ContractComplianceResult { ServiceContractId = serviceContractId };
			await EvaluateAsync(result, serviceContractId, departmentId, new List<DeploymentAttachment>(), null);
			return result;
		}

		private async Task EvaluateAsync(ContractComplianceResult result, string serviceContractId, int departmentId, List<DeploymentAttachment> attachments, DateTime? asOf)
		{
			var requirements = (await _requirements.GetByContractAsync(serviceContractId))?.OrderBy(r => r.SortOrder).ToList() ?? new List<ServiceContractDocumentRequirement>();
			var documents = (await _documents.GetForDepartmentAsync(departmentId))?.ToList() ?? new List<DepartmentComplianceDocument>();
			var now = asOf ?? DateTime.UtcNow;
			foreach (var requirement in requirements)
			{
				var item = new ContractComplianceItem
				{
					ServiceContractDocumentRequirementId = requirement.ServiceContractDocumentRequirementId, Name = requirement.Name, Stage = requirement.Stage,
					ComplianceDocumentType = requirement.ComplianceDocumentType, IsMandatory = requirement.IsMandatory
				};
				// A current department document of the requirement's type satisfies it; otherwise a deployment attachment whose
				// name contains the requirement name does (signed service requests, manifests, DTR PDFs).
				var document = requirement.ComplianceDocumentType.HasValue
					? documents.Where(d => d.DocumentType == requirement.ComplianceDocumentType.Value && d.IsCurrent(now)).OrderByDescending(d => d.ExpiresOn ?? DateTime.MaxValue).FirstOrDefault()
					: null;
				if (document != null)
				{
					item.Satisfied = true;
					item.SatisfiedBy = document.Name;
					item.DepartmentComplianceDocumentId = document.DepartmentComplianceDocumentId;
					item.ExpiresOn = document.ExpiresOn;
				}
				else
				{
					var attachment = attachments.FirstOrDefault(a => !a.IsDeleted && MatchesRequirement(a, requirement));
					if (attachment != null)
					{
						item.Satisfied = true;
						item.SatisfiedBy = attachment.Name;
						item.DeploymentAttachmentId = attachment.DeploymentAttachmentId;
					}
				}
				result.Items.Add(item);
			}
		}

		private static bool MatchesRequirement(DeploymentAttachment attachment, ServiceContractDocumentRequirement requirement)
		{
			if (requirement.Stage == (int)DocumentRequirementStages.DeploymentStart && attachment.AttachmentType == (int)DeploymentAttachmentTypes.SignedServiceRequest) return true;
			if (requirement.Stage == (int)DocumentRequirementStages.DailyTimeReport && attachment.AttachmentType == (int)DeploymentAttachmentTypes.TimeReportPdf) return true;
			if (requirement.Stage == (int)DocumentRequirementStages.InvoiceSubmission && attachment.AttachmentType == (int)DeploymentAttachmentTypes.Manifest && requirement.Name.IndexOf("manifest", StringComparison.OrdinalIgnoreCase) >= 0) return true;
			var name = attachment.Name;
			return !string.IsNullOrWhiteSpace(name) && name.IndexOf(requirement.Name, StringComparison.OrdinalIgnoreCase) >= 0;
		}

		#endregion

		#region Expiry sweep

		public async Task<int> RunExpirySweepAsync(DateTime asOfUtc, Func<int, Task<bool>> departmentEnabled = null, CancellationToken cancellationToken = default)
		{
			var touched = 0;
			var enabledCache = new Dictionary<int, bool>();
			async Task<bool> Enabled(int departmentId)
			{
				if (enabledCache.TryGetValue(departmentId, out var cached)) return cached;
				var enabled = departmentEnabled == null || await departmentEnabled(departmentId);
				enabledCache[departmentId] = enabled;
				return enabled;
			}

			// Lapsed: Active with EndOn in the past → Expired (one status event each).
			foreach (var contract in (await _contracts.GetLapsedAsync(asOfUtc))?.ToList() ?? new List<ServiceContract>())
			{
				if (!await Enabled(contract.DepartmentId)) continue;
				try
				{
					var before = Snapshot(contract);
					contract.Status = (int)ServiceContractStatuses.Expired;
					contract.EditedOn = asOfUtc;
					var saved = await _contracts.SaveOrUpdateAsync(contract, cancellationToken);
					if (_searchProjections?.Value != null) await _searchProjections.Value.ProjectServiceContractAsync(contract, cancellationToken);
					Audit(contract.DepartmentId, null, AuditLogTypes.ServiceContractStatusChanged, null, null, before, saved);
					await PublishAsync(saved, WorkflowTriggerEventType.ContractStatusChanged, (int)ServiceContractStatuses.Active, cancellationToken);
					touched++;
				}
				catch (Exception ex) { Logging.LogException(ex, $"Contract {contract.ServiceContractId} could not be expired."); }
			}

			// Expiring: Active with EndOn inside the lead window → ContractExpiring once per day per contract.
			var dayKey = asOfUtc.Date.GetHashCode();
			foreach (var contract in (await _contracts.GetEndingBetweenAsync(asOfUtc, asOfUtc.AddDays(ExpiryLeadDays)))?.ToList() ?? new List<ServiceContract>())
			{
				if (!await Enabled(contract.DepartmentId)) continue;
				var key = HashCode.Combine(dayKey, contract.ServiceContractId);
				lock (ExpiringAnnounced) { if (!ExpiringAnnounced.Add(key)) continue; }
				await PublishAsync(contract, WorkflowTriggerEventType.ContractExpiring, null, cancellationToken, daysUntilEnd: contract.EndOn.HasValue ? (int)Math.Ceiling((contract.EndOn.Value - asOfUtc).TotalDays) : (int?)null);
				touched++;
			}

			// Compliance documents inside their own lead window (or lapsed) → one admin notification per document per day.
			var expiring = (await _documents.GetExpiringAsync(asOfUtc))?.ToList() ?? new List<DepartmentComplianceDocument>();
			foreach (var group in expiring.GroupBy(d => d.DepartmentId))
			{
				if (!await Enabled(group.Key)) continue;
				var lines = new List<string>();
				foreach (var document in group)
				{
					var key = HashCode.Combine(dayKey, "doc", document.DepartmentComplianceDocumentId);
					lock (ExpiringAnnounced) { if (!ExpiringAnnounced.Add(key)) continue; }
					var days = document.ExpiresOn.HasValue ? (int)Math.Ceiling((document.ExpiresOn.Value.Date - asOfUtc.Date).TotalDays) : 0;
					lines.Add(days < 0 ? $"{document.Name} expired on {document.ExpiresOn:yyyy-MM-dd}." : $"{document.Name} expires in {days} day{(days == 1 ? "" : "s")} ({document.ExpiresOn:yyyy-MM-dd}).");
				}
				if (lines.Count > 0) await NotifyAdminsAsync(group.Key, "Compliance documents: " + string.Join(" ", lines));
			}
			lock (ExpiringAnnounced) { if (ExpiringAnnounced.Count > 50_000) ExpiringAnnounced.Clear(); }
			return touched;
		}

		private async Task NotifyAdminsAsync(int departmentId, string message)
		{
			if (_communication?.Value == null) return;
			try
			{
				var department = await _departmentsService.GetDepartmentByIdAsync(departmentId, false);
				var number = _departmentSettings?.Value == null ? null : await _departmentSettings.Value.GetTextToCallNumberForDepartmentAsync(departmentId);
				foreach (var admin in await _departmentsService.GetActiveAdminsForDepartmentAsync(departmentId))
					await _communication.Value.SendNotificationAsync(admin.UserId, departmentId, message, number, department, "Compliance documents");
			}
			catch (Exception ex) { Logging.LogException(ex, $"Compliance document notification for department {departmentId} failed."); }
		}

		#endregion

		#region Helpers

		private async Task PublishAsync(ServiceContract contract, WorkflowTriggerEventType trigger, int? oldStatus, CancellationToken cancellationToken, int? daysUntilEnd = null)
		{
			try
			{
				string contactName = null;
				try { var contact = await _contactsService.GetContactByIdAsync(contract.ContactId); contactName = contact?.Name; } catch (Exception ex) { Logging.LogException(ex, "Contract event: contact name lookup failed."); }
				await _outbox.EnqueueAsync(contract.DepartmentId, ContractorWorkflowPayload.Producer, new DomainEventEnvelope
				{
					EventName = trigger.ToString(),
					AggregateType = "ServiceContract",
					AggregateId = contract.ServiceContractId,
					AggregateVersion = 0,
					Trigger = trigger,
					OccurredOn = DateTime.UtcNow,
					CorrelationId = contract.ServiceContractId,
					Payload = new
					{
						contract.ServiceContractId, contract.ContractNumber, contract.Name, contract.Status, OldStatus = oldStatus, contract.ContactId,
						ContactName = ProtectedDataEnvelope.SafeDisplay(contactName),
						contract.ContractType, contract.StartOn, contract.EndOn, DaysUntilEnd = daysUntilEnd, contract.RateScheduleId
					}
				}, cancellationToken);
			}
			catch (Exception ex) { Logging.LogException(ex, $"Contract {contract.ServiceContractId} {trigger} could not be published."); }
		}

		private static string Trim(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

		internal static string Snapshot<T>(T entity)
		{
			var clone = entity.CloneJson();
			switch (clone)
			{
				case ServiceContract contract: contract.Requirements = null; break;
				case DepartmentComplianceDocument document: document.Data = null; break;
			}
			return clone.CloneJsonToString();
		}

		private void Audit<T>(int departmentId, string userId, AuditLogTypes type, string ipAddress, string userAgent, string before, T after)
		{
			var audit = DeploymentService.NewAuditEvent(departmentId, userId, type, ipAddress, userAgent);
			audit.Before = before;
			audit.After = after == null ? null : Snapshot(after);
			_eventAggregator.SendMessage<AuditEvent>(audit);
		}

		#endregion
	}
}
