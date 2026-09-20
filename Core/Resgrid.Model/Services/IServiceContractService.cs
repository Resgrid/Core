using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Invoicing;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Service contracts and department compliance documents (Workforce &amp; Business Operations plan, C4;
	/// decisions 14, 22, 24). Status transitions publish <see cref="WorkflowTriggerEventType.ContractStatusChanged"/>,
	/// the expiry sweep publishes <see cref="WorkflowTriggerEventType.ContractExpiring"/>, compliance is evaluated
	/// warn-only in v1. Callers authorize.
	/// </summary>
	public interface IServiceContractService
	{
		Task<List<ServiceContract>> GetContractsForDepartmentAsync(int departmentId, ServiceContractStatuses? status = null);
		Task<List<ServiceContract>> GetContractsByContactIdAsync(string contactId, int departmentId);
		/// <summary>The contract with its document requirements; null when missing or deleted.</summary>
		Task<ServiceContract> GetContractByIdAsync(string serviceContractId, int departmentId);
		Task<ServiceContract> SaveContractAsync(ServiceContract contract, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<ServiceContract> SetContractStatusAsync(string serviceContractId, int departmentId, ServiceContractStatuses status, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<bool> DeleteContractAsync(string serviceContractId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		/// <summary>Replaces the contract's document requirements with the given set.</summary>
		Task<List<ServiceContractDocumentRequirement>> SaveRequirementsAsync(string serviceContractId, int departmentId, List<ServiceContractDocumentRequirement> requirements, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);

		/// <summary>Department compliance documents without bytes.</summary>
		Task<List<DepartmentComplianceDocument>> GetComplianceDocumentsAsync(int departmentId);
		Task<DepartmentComplianceDocument> GetComplianceDocumentAsync(int departmentComplianceDocumentId, int departmentId, bool includeData);
		/// <summary>Saves the document row; <paramref name="data"/> replaces the stored file when supplied.</summary>
		Task<DepartmentComplianceDocument> SaveComplianceDocumentAsync(DepartmentComplianceDocument document, byte[] data, string fileName, string contentType, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);
		Task<bool> DeleteComplianceDocumentAsync(int departmentComplianceDocumentId, int departmentId, string userId, string ipAddress, string userAgent, CancellationToken cancellationToken = default);

		/// <summary>Evaluates the deployment's contract requirements against its attachments and the department's current compliance documents (warn-only).</summary>
		Task<ContractComplianceResult> GetContractComplianceAsync(string deploymentId, int departmentId);
		/// <summary>The same evaluation for a contract without a deployment (requirements at BidSubmission / InvoiceSubmission stage).</summary>
		Task<ContractComplianceResult> GetContractComplianceForContractAsync(string serviceContractId, int departmentId);

		/// <summary>Daily sweep: contracts ending within their lead window publish <c>ContractExpiring</c> once per day, lapsed ones move to Expired; expiring compliance documents notify department admins. Returns the number of contracts touched.</summary>
		Task<int> RunExpirySweepAsync(DateTime asOfUtc, Func<int, Task<bool>> departmentEnabled = null, CancellationToken cancellationToken = default);
	}
}
