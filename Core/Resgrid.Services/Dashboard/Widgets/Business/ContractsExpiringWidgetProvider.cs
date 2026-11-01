using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Model.Dashboard;
using Resgrid.Model.Dashboard.Widgets.Business;
using Resgrid.Model.Invoicing;
using Resgrid.Model.Services;

namespace Resgrid.Services.Dashboard.Widgets.Business
{
	/// <summary>
	/// contracts.expiring (plan §7.9): active contracts ending inside the window and the department compliance documents that lapse in
	/// it. Two metadata reads (contracts; compliance documents without their file data). Per-contract compliance evaluation is left to
	/// the contract page (two reads per contract would break the budget).
	/// </summary>
	[DashboardWidget("contracts.expiring")]
	public class ContractsExpiringWidgetProvider : IDashboardWidgetDataProvider
	{
		private readonly IServiceContractService _serviceContractService;

		public ContractsExpiringWidgetProvider(IServiceContractService serviceContractService)
		{
			_serviceContractService = serviceContractService;
		}

		public async Task<object> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
		{
			var departmentId = context.DepartmentId;
			var days = context.GetInt("days", 60);
			var max = context.GetInt("maxRows", 5);
			var now = context.NowUtc;
			var horizon = now.AddDays(days);

			var live = (await _serviceContractService.GetContractsForDepartmentAsync(departmentId, ServiceContractStatuses.Active) ?? new List<ServiceContract>())
				.Where(c => c.DepartmentId == departmentId && c.IsLive(now)).ToList();
			var expiring = live.Where(c => c.EndOn.HasValue && c.EndOn.Value <= horizon).OrderBy(c => c.EndOn).ToList();

			var documents = (await _serviceContractService.GetComplianceDocumentsAsync(departmentId) ?? new List<DepartmentComplianceDocument>())
				.Where(d => d.DepartmentId == departmentId && !d.IsDeleted && d.ExpiresOn.HasValue).ToList();
			var today = now.Date;

			return new ContractsExpiringWidgetData
			{
				Days = days,
				Active = live.Count,
				ExpiringCount = expiring.Count,
				Rows = expiring.Take(max).Select(c => new ContractsExpiringRow
				{
					ServiceContractId = c.ServiceContractId,
					Name = c.Name,
					ContractNumber = c.ContractNumber,
					EndOn = c.EndOn.Value,
					DaysLeft = (int)(c.EndOn.Value.Date - today).TotalDays
				}).ToList(),
				ComplianceDocumentsExpired = documents.Count(d => d.ExpiresOn.Value.Date < today),
				ComplianceDocumentsExpiring = documents.Count(d => d.ExpiresOn.Value.Date >= today && d.ExpiresOn.Value <= horizon)
			};
		}
	}
}
