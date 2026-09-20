using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model.Services;
using Resgrid.Model.Workforce;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4;
using Resgrid.Web.Services.Models.v4.Workforce;
using Resgrid.Web.ServicesCore.Helpers;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Internal field costing (Workforce &amp; Business Operations plan, Phase E / E6). Gated by the
	/// Workforce.InternalCosting entitlement. InternalCosts_View (74) exposes aggregate cost summaries for a deployment
	/// or call — categories, totals, revenue and margin only; rostered members file their own resource usage readings
	/// for a deployment they are seated on. Compensation, cost lines, pay data and every protected value stay MVC-only.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	[Authorize]
	public class FieldCostController : V4AuthenticatedApiControllerbase
	{
		private readonly IFieldCostingService _costing;
		private readonly IDeploymentService _deployments;
		private readonly IBusinessOperationsAccessService _access;

		public FieldCostController(IFieldCostingService costing, IDeploymentService deployments, IBusinessOperationsAccessService access)
		{
			_costing = costing;
			_deployments = deployments;
			_access = access;
		}

		private Task<bool> EnabledAsync() => _access.CanUseWorkforceAsync(DepartmentId);
		private static bool IsAdmin => ClaimsAuthorizationHelper.IsUserDepartmentAdmin();
		private static bool CanViewInternalCosts => IsAdmin || ClaimsAuthorizationHelper.CanViewInternalCosts();
		private string Ip => IpAddressHelper.GetRequestIP(Request, true);
		private string Agent => $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}";
		private static bool IsDomainError(InvalidOperationException ex) => ex.Message.StartsWith("workforce_", StringComparison.Ordinal);

		private ActionResult<T> Failed<T>(string reason, int status = StatusCodes.Status400BadRequest) where T : StandardApiResponseV4Base, new()
		{
			var failed = new T { PageSize = 0, Status = ResponseHelper.Failure };
			ResponseHelper.PopulateV4ResponseData(failed);
			Response.Headers["X-Resgrid-Reason"] = reason;
			return StatusCode(status, failed);
		}

		private async Task<bool> IsRosteredAsync(string deploymentId)
		{
			if (string.IsNullOrWhiteSpace(deploymentId)) return false;
			var mine = await _deployments.GetDeploymentsForUserAsync(DepartmentId, UserId, false);
			return mine?.Any(d => string.Equals(d.DeploymentId, deploymentId, StringComparison.OrdinalIgnoreCase)) == true;
		}

		private async Task<bool> IsUnitOnDeploymentAsync(string deploymentId, int unitId)
		{
			if (string.IsNullOrWhiteSpace(deploymentId) || unitId <= 0) return false;
			var deployment = await _deployments.GetDeploymentByIdAsync(deploymentId, DepartmentId);
			return deployment?.Units?.Any(u => u.IsActive && u.UnitId == unitId) == true;
		}

		[HttpGet("GetAccess")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<FieldCostAccessResult>> GetAccess()
		{
			var enabled = await EnabledAsync();
			var result = new FieldCostAccessResult { Data = new FieldCostAccessData { Enabled = enabled, CanViewInternalCosts = enabled && CanViewInternalCosts, CanRecordUsage = enabled }, PageSize = 1, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>Aggregate summaries of every run for a deployment or call (ViewInternalCosts). Never a line, rate or person.</summary>
		[HttpGet("GetFieldCostSummaries")]
		[Authorize(Policy = ResgridResources.InternalCosts_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<FieldCostSummariesResult>> GetFieldCostSummaries(string deploymentId = null, int? callId = null)
		{
			if (!await EnabledAsync()) return Failed<FieldCostSummariesResult>("workforce_disabled", StatusCodes.Status403Forbidden);
			if (string.IsNullOrWhiteSpace(deploymentId) && !callId.HasValue) return Failed<FieldCostSummariesResult>("workforce_usage_context_required");
			var runs = !string.IsNullOrWhiteSpace(deploymentId) ? await _costing.GetRunsForDeploymentAsync(deploymentId, DepartmentId) : await _costing.GetRunsForCallAsync(callId.Value, DepartmentId);
			var result = new FieldCostSummariesResult { Data = runs.Select(Map).ToList(), PageSize = runs.Count, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		[HttpGet("GetFieldCostSummary")]
		[Authorize(Policy = ResgridResources.InternalCosts_View)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<FieldCostSummaryResult>> GetFieldCostSummary(string runId)
		{
			if (!await EnabledAsync()) return Failed<FieldCostSummaryResult>("workforce_disabled", StatusCodes.Status403Forbidden);
			var summary = await _costing.GetFieldCostSummaryAsync(runId, DepartmentId);
			if (summary == null) return Failed<FieldCostSummaryResult>("workforce_not_found", StatusCodes.Status404NotFound);
			var run = await _costing.GetRunAsync(runId, DepartmentId);
			var result = new FieldCostSummaryResult { Data = Map(run), PageSize = 1, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>Usage entries for a deployment the caller is rostered on (or any, with ViewInternalCosts).</summary>
		[HttpGet("GetResourceUsage")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<ResourceUsagesResult>> GetResourceUsage(string deploymentId)
		{
			if (!await EnabledAsync()) return Failed<ResourceUsagesResult>("workforce_disabled", StatusCodes.Status403Forbidden);
			if (!CanViewInternalCosts && !await IsRosteredAsync(deploymentId)) return Failed<ResourceUsagesResult>("workforce_not_rostered", StatusCodes.Status403Forbidden);
			var rows = await _costing.GetUsageForDeploymentAsync(deploymentId, DepartmentId);
			var result = new ResourceUsagesResult { Data = rows.Select(Map).ToList(), PageSize = rows.Count, Status = ResponseHelper.Success };
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>A rostered member's own reading for a unit on their deployment (or any, with ViewInternalCosts). Distance is canonicalised to miles; conflicting readings are queued for review.</summary>
		[HttpPost("AddResourceUsage")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<ResourceUsageResult>> AddResourceUsage([FromBody] AddResourceUsageInput input)
		{
			if (!await EnabledAsync()) return Failed<ResourceUsageResult>("workforce_disabled", StatusCodes.Status403Forbidden);
			if (input == null) return Failed<ResourceUsageResult>("workforce_usage_invalid");
			if (!CanViewInternalCosts && !await IsRosteredAsync(input.DeploymentId)) return Failed<ResourceUsageResult>("workforce_not_rostered", StatusCodes.Status403Forbidden);
			// A rostered member files readings for the units on their own deployment only; any unit needs ViewInternalCosts.
			if (!CanViewInternalCosts && !await IsUnitOnDeploymentAsync(input.DeploymentId, input.UnitId)) return Failed<ResourceUsageResult>("workforce_unit_not_on_deployment", StatusCodes.Status403Forbidden);
			try
			{
				var entry = new ResourceUsageEntry
				{
					DepartmentId = DepartmentId, SubjectType = (int)ResourceSubjectTypes.Unit, UnitId = input.UnitId, DeploymentId = input.DeploymentId, CallId = input.CallId, UsageDate = input.UsageDate, Phase = input.Phase,
					StartOdometer = input.StartOdometer, EndOdometer = input.EndOdometer, DistanceUnit = input.DistanceUnit, OriginalDistance = input.Distance, StartEngineMeter = input.StartEngineMeter, EndEngineMeter = input.EndEngineMeter,
					EngineHours = input.EngineHours, OperatingHours = input.OperatingHours, IdleHours = input.IdleHours, FuelQuantity = input.FuelQuantity, FuelUnit = input.FuelUnit, FuelActualCost = input.FuelActualCost,
					Source = (int)UsageSources.Manual, ExternalId = input.ExternalId, IsApproved = false
				};
				var saved = await _costing.SaveUsageEntryAsync(entry, UserId, Ip, Agent);
				var result = new ResourceUsageResult { Data = Map(saved), PageSize = 1, Status = ResponseHelper.Success };
				ResponseHelper.PopulateV4ResponseData(result);
				return result;
			}
			catch (InvalidOperationException ex) when (IsDomainError(ex)) { return Failed<ResourceUsageResult>(ex.Message); }
		}

		private static FieldCostSummaryData Map(FieldCostRun run) => new FieldCostSummaryData
		{
			RunId = run.FieldCostRunId, ContextType = run.ContextType, DeploymentId = run.DeploymentId, BidId = run.BidId, CallId = run.CallId, RunType = run.RunType, Status = run.Status, ThroughDate = run.ThroughDate, Currency = run.Currency,
			PersonnelTotal = run.PersonnelTotal, ResourceTotal = run.ResourceTotal, ConsumableTotal = run.ConsumableTotal, ExpenseTotal = run.ExpenseTotal, OverheadTotal = run.OverheadTotal, TotalLoadedCost = run.TotalLoadedCost,
			RevenueSource = run.RevenueSource, RevenueAmount = run.RevenueAmount, ContributionMargin = run.ContributionMargin, ContributionMarginPercent = run.ContributionMarginPercent, BreakEvenRevenue = run.BreakEvenRevenue,
			MissingInputCount = run.MissingInputCount, FrozenOn = run.FrozenOn, CreatedOn = run.AddedOn
		};

		private static ResourceUsageData Map(ResourceUsageEntry u) => new ResourceUsageData
		{
			Id = u.ResourceUsageEntryId, DeploymentId = u.DeploymentId, CallId = u.CallId, UnitId = u.UnitId ?? 0, UsageDate = u.UsageDate, Phase = u.Phase, DistanceUnit = u.DistanceUnit, OriginalDistance = u.OriginalDistance, CanonicalDistanceMiles = u.CanonicalDistanceMiles,
			EngineHours = u.EngineHours, OperatingHours = u.OperatingHours, IdleHours = u.IdleHours, FuelQuantity = u.FuelQuantity, FuelUnit = u.FuelUnit, FuelActualCost = u.FuelActualCost, Source = u.Source, NeedsReview = u.NeedsReview, ReviewReason = u.ReviewReason, IsApproved = u.IsApproved
		};
	}
}
