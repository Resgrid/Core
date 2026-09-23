using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Invoicing;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;

namespace Resgrid.Services.Invoicing
{
	/// <summary>
	/// Contractor billing engine (Workforce &amp; Business Operations plan, C4). Loads the deployment graph, the
	/// effective rate schedule (deployment → contract → profile → department default) and the approved unbilled
	/// DTRs, runs <see cref="ContractorChargeCalculator"/>, and turns the result into a Phase B draft invoice whose
	/// lines carry DTR provenance. The packet builder bundles the supporting documents a contract requires at invoice
	/// submission. Callers authorize.
	/// </summary>
	public class ContractorBillingEngine : IContractorBillingEngine
	{
		private readonly IDeploymentService _deployments;
		private readonly ITimeTrackingService _timeTracking;
		private readonly IDeploymentTimeEntryRepository _entries;
		private readonly IRateScheduleService _rateSchedules;
		private readonly IServiceContractService _contracts;
		private readonly IInvoicingService _invoicing;
		private readonly IUnitsService _unitsService;
		private readonly IUserProfileService _userProfileService;
		private readonly IEventAggregator _eventAggregator;
		private readonly IUnitOfWork _unitOfWork;
		private readonly IDeploymentTimeReportRepository _reports;
		private readonly IDeploymentRepository _deploymentRows;
		private readonly IDepartmentsService _departmentsService;
		private readonly Lazy<ICommunicationService> _communication;
		private readonly Lazy<IDepartmentSettingsService> _departmentSettings;
		private static readonly HashSet<int> RemindedToday = new HashSet<int>();

		public ContractorBillingEngine(IDeploymentService deployments, ITimeTrackingService timeTracking, IDeploymentTimeEntryRepository entries, IRateScheduleService rateSchedules,
			IServiceContractService contracts, IInvoicingService invoicing, IUnitsService unitsService, IUserProfileService userProfileService, IEventAggregator eventAggregator, IUnitOfWork unitOfWork,
			IDeploymentTimeReportRepository reports = null, IDeploymentRepository deploymentRows = null, IDepartmentsService departmentsService = null,
			Lazy<ICommunicationService> communication = null, Lazy<IDepartmentSettingsService> departmentSettings = null)
		{
			_reports = reports;
			_deploymentRows = deploymentRows;
			_departmentsService = departmentsService;
			_communication = communication;
			_departmentSettings = departmentSettings;
			_deployments = deployments;
			_timeTracking = timeTracking;
			_entries = entries;
			_rateSchedules = rateSchedules;
			_contracts = contracts;
			_invoicing = invoicing;
			_unitsService = unitsService;
			_userProfileService = userProfileService;
			_eventAggregator = eventAggregator;
			_unitOfWork = unitOfWork;
		}

		public async Task<ContractorChargeSet> CalculateDeploymentChargesAsync(string deploymentId, int departmentId, DateTime? throughDate = null)
		{
			var input = await BuildInputAsync(deploymentId, departmentId, throughDate);
			return input == null ? null : ContractorChargeCalculator.Calculate(input);
		}

		/// <summary>Everything the pure calculator needs; null when the deployment does not exist.</summary>
		internal async Task<ContractorChargeInput> BuildInputAsync(string deploymentId, int departmentId, DateTime? throughDate)
		{
			var deployment = await _deployments.GetDeploymentByIdAsync(deploymentId, departmentId);
			if (deployment == null) return null;

			var contract = string.IsNullOrWhiteSpace(deployment.ServiceContractId) ? null : await _contracts.GetContractByIdAsync(deployment.ServiceContractId, departmentId);
			var schedule = string.IsNullOrWhiteSpace(deployment.RateScheduleId) ? null : await _rateSchedules.GetScheduleByIdAsync(deployment.RateScheduleId, departmentId, includeInactive: true);
			schedule ??= await _rateSchedules.GetEffectiveScheduleForContactAsync(deployment.ContactId, departmentId, deployment.ServiceContractId);

			var reports = (await _timeTracking.GetUnbilledApprovedReportsAsync(departmentId, deploymentId))
				.Where(r => !throughDate.HasValue || r.ReportDate.Date <= throughDate.Value.Date).OrderBy(r => r.ReportDate).ToList();
			var entries = (await _entries.GetByDeploymentAsync(deploymentId))?.ToList() ?? new List<DeploymentTimeEntry>();
			foreach (var report in reports)
				report.Entries = entries.Where(e => string.Equals(e.DeploymentTimeReportId, report.DeploymentTimeReportId, StringComparison.OrdinalIgnoreCase)).OrderBy(e => e.StartTime).ToList();

			var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (var unit in deployment.Units) names[unit.DeploymentUnitId] = unit.UnitName ?? unit.CallSign ?? $"Unit {unit.UnitId}";
			foreach (var person in deployment.Personnel) names[person.DeploymentPersonnelId] = person.DisplayName ?? person.CallSign ?? person.UserId;
			foreach (var equipment in deployment.Equipment) names[equipment.DeploymentEquipmentId] = equipment.FreeTextName ?? equipment.InventoryAssetId ?? equipment.InventoryItemId ?? "Equipment";

			return new ContractorChargeInput
			{
				Deployment = deployment,
				Schedule = schedule,
				Reports = reports,
				Expenses = await _timeTracking.GetExpensesAsync(deploymentId, departmentId),
				Personnel = deployment.Personnel,
				Units = deployment.Units,
				Equipment = deployment.Equipment,
				SubjectNames = names,
				CancellationDate = deployment.Status == (int)DeploymentStatuses.Cancelled ? LocalDate(deployment.StatusChangedOn, deployment.LocalTimeZoneId) : null,
				// Decision 14: deployment (from the bid) → contract; the profile default is already on the draft when neither applies.
				DiscountPercent = deployment.DiscountPercent ?? contract?.DiscountPercent
			};
		}

		public async Task<Invoice> GenerateInvoiceFromDeploymentAsync(string deploymentId, int departmentId, DateTime? throughDate, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var input = await BuildInputAsync(deploymentId, departmentId, throughDate);
			if (input == null) throw new InvalidOperationException("deployments_not_found");
			var deployment = input.Deployment;
			if (deployment.FinanceMode != (int)DeploymentFinanceModes.Billable) throw new InvalidOperationException("contractor_deployment_not_billable");
			if (string.IsNullOrWhiteSpace(deployment.ContactId)) throw new InvalidOperationException("contractor_deployment_no_contact");
			var charges = ContractorChargeCalculator.Calculate(input);
			if (!charges.HasCharges) throw new InvalidOperationException("contractor_no_charges");
			var contract = string.IsNullOrWhiteSpace(deployment.ServiceContractId) ? null : await _contracts.GetContractByIdAsync(deployment.ServiceContractId, departmentId);

			return await TransactionAsync(async () =>
			{
				var invoice = await _invoicing.CreateDraftInvoiceAsync(departmentId, deployment.ContactId, userId, ipAddress, userAgent, charges.Currency, cancellationToken);
				invoice = await _invoicing.LinkInvoiceToDeploymentAsync(invoice.InvoiceId, departmentId, deployment.DeploymentId, deployment.ServiceContractId, contract?.TermsNetDays, userId, ipAddress, userAgent, cancellationToken);

				if (charges.DiscountPercent.HasValue && charges.DiscountPercent != invoice.DiscountPercent)
				{
					invoice.DiscountPercent = charges.DiscountPercent;
					invoice = await _invoicing.SaveInvoiceAsync(invoice, userId, ipAddress, userAgent, cancellationToken);
				}

				var lines = charges.Lines.Select((line, index) => new InvoiceLineItem
				{
					InvoiceId = invoice.InvoiceId, DepartmentId = departmentId, CallId = deployment.CallId, DeploymentTimeReportId = line.DeploymentTimeReportId,
					Description = line.Description, Quantity = line.Quantity, UnitRate = line.UnitRate, Amount = line.Amount, Taxable = line.Taxable, SortOrder = index
				}).ToList();
				invoice = await _invoicing.SaveInvoiceLineItemsAsync(invoice.InvoiceId, departmentId, lines, userId, ipAddress, userAgent, cancellationToken);

				await _timeTracking.MarkTimeReportsBilledAsync(charges.ReportIds, departmentId, invoice.InvoiceId, userId, ipAddress, userAgent, cancellationToken);

				var audit = DeploymentService.NewAuditEvent(departmentId, userId, AuditLogTypes.DeploymentInvoiceGenerated, ipAddress, userAgent);
				audit.After = new { deployment.DeploymentId, invoice.InvoiceId, invoice.InvoiceNumber, Reports = charges.ReportIds, Lines = lines.Count, charges.SubTotal, Warnings = charges.Warnings.Select(w => w.Code).Distinct().ToList() }.CloneJsonToString();
				_eventAggregator.SendMessage<AuditEvent>(audit);
				return await _invoicing.GetInvoiceByIdAsync(invoice.InvoiceId, departmentId);
			}, cancellationToken);
		}

		#region Packet

		public async Task<ContractorInvoicePacket> BuildInvoicePacketAsync(string invoiceId, int departmentId)
		{
			var invoice = await _invoicing.GetInvoiceByIdAsync(invoiceId, departmentId);
			if (invoice == null) throw new InvalidOperationException("invoicing_invoice_not_found");
			var packet = new ContractorInvoicePacket { FileName = $"invoice-{invoice.InvoiceNumber}-packet.zip" };

			using var stream = new MemoryStream();
			using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
			{
				var invoicePdf = await _invoicing.GetInvoicePdfAsync(invoiceId, departmentId);
				if (invoicePdf != null && invoicePdf.Length > 0) Add(zip, packet, $"invoice-{invoice.InvoiceNumber}.pdf", invoicePdf);

				if (!string.IsNullOrWhiteSpace(invoice.DeploymentId))
				{
					var deployment = await _deployments.GetDeploymentByIdAsync(invoice.DeploymentId, departmentId);
					var reportIds = (invoice.LineItems ?? new List<InvoiceLineItem>()).Select(l => l.DeploymentTimeReportId).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
					foreach (var reportId in reportIds)
					{
						try
						{
							var report = await _timeTracking.GetTimeReportByIdAsync(reportId, departmentId);
							var pdf = report == null ? null : await _timeTracking.GetTimeReportPdfAsync(reportId, departmentId);
							if (pdf != null && pdf.Length > 0) Add(zip, packet, $"dtr/dtr-{report.ReportNumber}-{report.ReportDate:yyyy-MM-dd}.pdf", pdf);
						}
						catch (Exception ex) { Logging.LogException(ex, $"Invoice packet: DTR {reportId} PDF skipped."); }
					}

					if (deployment != null)
					{
						// Receipts referenced by billable expenses on the billed reports, plus the manifest when the contract asks for one.
						var expenses = (await _timeTracking.GetExpensesAsync(invoice.DeploymentId, departmentId)).Where(e => e.Billable && e.ReceiptAttachmentId.HasValue && (string.IsNullOrWhiteSpace(e.DeploymentTimeReportId) || reportIds.Contains(e.DeploymentTimeReportId, StringComparer.OrdinalIgnoreCase))).ToList();
						foreach (var expense in expenses)
							await AddAttachmentAsync(zip, packet, departmentId, expense.ReceiptAttachmentId.Value, "receipts");

						var compliance = await _contracts.GetContractComplianceAsync(invoice.DeploymentId, departmentId);
						foreach (var item in compliance?.Items.Where(i => i.Stage == (int)DocumentRequirementStages.InvoiceSubmission) ?? Enumerable.Empty<ContractComplianceItem>())
						{
							if (!item.Satisfied) { packet.MissingRequirements.Add(item.Name); continue; }
							if (item.DepartmentComplianceDocumentId.HasValue)
							{
								var document = await _contracts.GetComplianceDocumentAsync(item.DepartmentComplianceDocumentId.Value, departmentId, includeData: true);
								if (document?.Data != null && document.Data.Length > 0) Add(zip, packet, $"compliance/{Safe(document.FileName ?? $"{document.Name}.bin")}", document.Data);
								else packet.MissingRequirements.Add($"{item.Name} (no file)");
							}
							else if (item.DeploymentAttachmentId.HasValue)
							{
								if (!await AddAttachmentAsync(zip, packet, departmentId, item.DeploymentAttachmentId.Value, "documents"))
									packet.MissingRequirements.Add($"{item.Name} (no file)");
							}
						}
					}
				}
			}
			packet.Data = stream.ToArray();
			return packet;
		}

		private async Task<bool> AddAttachmentAsync(ZipArchive zip, ContractorInvoicePacket packet, int departmentId, int attachmentId, string folder)
		{
			try
			{
				var attachment = await _deployments.GetAttachmentAsync(attachmentId, departmentId, includeData: true);
				if (attachment?.Data == null || attachment.Data.Length == 0) return false;
				var name = attachment.FileName ?? $"attachment-{attachmentId}.bin";
				Add(zip, packet, $"{folder}/{Safe(name)}", attachment.Data);
				return true;
			}
			catch (Exception ex) { Logging.LogException(ex, $"Invoice packet: attachment {attachmentId} skipped."); return false; }
		}

		private static void Add(ZipArchive zip, ContractorInvoicePacket packet, string path, byte[] data)
		{
			var unique = path;
			var n = 1;
			while (packet.Contents.Contains(unique, StringComparer.OrdinalIgnoreCase))
				unique = Path.ChangeExtension(path, null) + $"-{++n}" + Path.GetExtension(path);
			var entry = zip.CreateEntry(unique, CompressionLevel.Optimal);
			using var target = entry.Open();
			target.Write(data, 0, data.Length);
			packet.Contents.Add(unique);
		}

		private static string Safe(string fileName)
		{
			var invalid = Path.GetInvalidFileNameChars();
			var cleaned = new string((fileName ?? "file").Select(c => invalid.Contains(c) || c == '/' || c == '\\' ? '_' : c).ToArray()).Trim();
			return string.IsNullOrWhiteSpace(cleaned) ? "file" : cleaned;
		}

		public async Task<Invoice> SendDeploymentInvoiceAsync(string invoiceId, int departmentId, string toEmail, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default)
		{
			var invoice = await _invoicing.GetInvoiceByIdAsync(invoiceId, departmentId);
			if (invoice == null) throw new InvalidOperationException("invoicing_invoice_not_found");
			var recipient = toEmail;
			if (string.IsNullOrWhiteSpace(recipient) && !string.IsNullOrWhiteSpace(invoice.ServiceContractId))
			{
				var contract = await _contracts.GetContractByIdAsync(invoice.ServiceContractId, departmentId);
				if (!string.IsNullOrWhiteSpace(contract?.InvoiceSubmissionEmail)) recipient = contract.InvoiceSubmissionEmail;
			}
			var packet = await BuildInvoicePacketAsync(invoiceId, departmentId);
			var attachment = new InvoiceSendAttachment { FileName = packet.FileName, ContentType = "application/zip", Data = packet.Data, Contents = packet.Contents };
			return await _invoicing.SendInvoiceAsync(invoiceId, departmentId, recipient, attachment, userId, ipAddress, userAgent, cancellationToken);
		}

		#endregion

		#region Reminder sweep

		public async Task<int> RunFinanceReminderSweepAsync(DateTime asOfUtc, int unbilledDays, Func<int, Task<bool>> departmentEnabled = null, CancellationToken cancellationToken = default)
		{
			if (_reports == null || _deploymentRows == null || _departmentsService == null || _communication?.Value == null) return 0;
			// One system-wide read, split on ApprovedOn (the repository already excludes null ApprovedOn and uses an inclusive bound).
			var cutoff = asOfUtc.AddDays(-Math.Max(0, unbilledDays));
			var unbilled = (await _reports.GetUnbilledApprovedBeforeAsync(asOfUtc))?.ToList() ?? new List<DeploymentTimeReport>();
			var stale = unbilled.Where(r => r.ApprovedOn.HasValue && r.ApprovedOn.Value <= cutoff).ToList();
			var recent = unbilled.Where(r => !r.ApprovedOn.HasValue || r.ApprovedOn.Value > cutoff).ToList();
			var notified = 0;
			var dayKey = asOfUtc.Date.GetHashCode();
			foreach (var group in stale.Concat(recent).GroupBy(r => r.DepartmentId))
			{
				if (departmentEnabled != null && !await departmentEnabled(group.Key)) continue;
				var key = HashCode.Combine(dayKey, group.Key);
				lock (RemindedToday) { if (RemindedToday.Contains(key)) continue; }
				var deployments = (await _deploymentRows.GetByIdsAsync(group.Key, group.Select(r => r.DeploymentId).Distinct()))?.Where(d => d.FinanceMode == (int)DeploymentFinanceModes.Billable).ToList() ?? new List<Deployment>();
				var lines = new List<string>();
				foreach (var deployment in deployments)
				{
					var staleCount = stale.Count(r => r.DeploymentId == deployment.DeploymentId);
					var openCount = group.Count(r => r.DeploymentId == deployment.DeploymentId);
					var completed = deployment.Status is (int)DeploymentStatuses.Completed or (int)DeploymentStatuses.Cancelled;
					if (staleCount == 0 && !completed) continue;
					lines.Add(completed
						? $"{deployment.Name}: {openCount} approved time report(s) unbilled on a completed deployment."
						: $"{deployment.Name}: {staleCount} approved time report(s) unbilled for more than {unbilledDays} days.");
				}
				if (lines.Count == 0) continue;
				lock (RemindedToday) { RemindedToday.Add(key); if (RemindedToday.Count > 50_000) RemindedToday.Clear(); }
				await NotifyAdminsAsync(group.Key, "Deployment billing: " + string.Join(" ", lines));
				notified++;
			}
			return notified;
		}

		private async Task NotifyAdminsAsync(int departmentId, string message)
		{
			try
			{
				var department = await _departmentsService.GetDepartmentByIdAsync(departmentId, false);
				var number = _departmentSettings?.Value == null ? null : await _departmentSettings.Value.GetTextToCallNumberForDepartmentAsync(departmentId);
				foreach (var admin in await _departmentsService.GetActiveAdminsForDepartmentAsync(departmentId))
					await _communication.Value.SendNotificationAsync(admin.UserId, departmentId, message, number, department, "Deployment billing");
			}
			catch (Exception ex) { Logging.LogException(ex, $"Deployment billing reminder for department {departmentId} failed."); }
		}

		#endregion

		#region Helpers

		private static DateTime? LocalDate(DateTime? utc, string timeZone)
		{
			if (!utc.HasValue) return null;
			try { return string.IsNullOrWhiteSpace(timeZone) ? utc.Value.Date : DateTimeHelpers.GetLocalDateTime(utc.Value, timeZone).Date; }
			catch { return utc.Value.Date; }
		}

		private async Task<T> TransactionAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
		{
			if (_unitOfWork == null || _unitOfWork.Transaction != null) return await action();
			try
			{
				await _unitOfWork.CreateOrGetConnectionAsync(cancellationToken);
				var result = await action();
				_unitOfWork.CommitChanges();
				return result;
			}
			catch
			{
				_unitOfWork.DiscardChanges();
				throw;
			}
		}

		#endregion
	}
}
