using System.Threading.Tasks;

namespace Resgrid.Model.Services
{
	/// <summary>
	/// Entitlement checks for the paid Business Operations add-on surfaces (Workforce &amp; Business Operations plan,
	/// decision 42). Each check is master flag → capability flag → module switch → billing configured → live
	/// PaymentAddons window, exactly as ReadinessAccessService.CanUseMaintenanceAsync; no entitlement cache.
	/// Free surfaces (pre-plans, certifications, the deployment core) never call this service.
	/// </summary>
	public interface IBusinessOperationsAccessService
	{
		/// <summary>The department may create and work invoices, rate cards and billing profiles (Phase B).</summary>
		Task<bool> CanUseInvoicingAsync(int departmentId);

		/// <summary>The department may use rate schedules, contracts, bids and contractor invoice generation (Phase C).</summary>
		Task<bool> CanUseContractorBillingAsync(int departmentId);

		/// <summary>The department may use the Cal OES MARS cost-recovery workspace (Phase C).</summary>
		Task<bool> CanUseCostRecoveryAsync(int departmentId);

		/// <summary>The department may use protected workforce pay data and field costing (Phase E, Workforce.InternalCosting).</summary>
		Task<bool> CanUseWorkforceAsync(int departmentId);

		/// <summary>The department may use California pay data reporting (Phase E, Compliance.CaliforniaPayDataReporting); additionally requires the department's Advanced Data Protection state to be Enabled.</summary>
		Task<bool> CanUsePayDataReportingAsync(int departmentId);

		/// <summary>The department holds an active Business Operations add-on window right now (no flag or module checks).</summary>
		Task<bool> HasActiveAddonAsync(int departmentId);
	}
}
