using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.FeatureToggles;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Built-in feature toggle API. The poll endpoints are available to any authenticated user and are
	/// scoped to that user's department. Flag/targeting/prerequisite/analytics management requires
	/// SystemAdmin; per-department override management is available to system administrators (any
	/// department) and to a department's own administrators (their department only).
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class FeatureTogglesController : V4AuthenticatedApiControllerbase
	{
		private readonly IFeatureToggleService _featureToggleService;

		public FeatureTogglesController(IFeatureToggleService featureToggleService)
		{
			_featureToggleService = featureToggleService;
		}

		private bool IsSystemAdmin => User.IsInRole("Admins");

		private bool IsDepartmentAdmin => User.HasClaim(ResgridClaimTypes.Resources.Department, ResgridClaimTypes.Actions.Update);

		#region Poll / evaluation (any authenticated user, scoped to the caller's department)

		/// <summary>Evaluates every active flag for the caller's department. Supports ETag/If-None-Match polling.</summary>
		[HttpGet("GetAll")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status304NotModified)]
		[Authorize]
		public async Task<ActionResult<FeatureTogglesResult>> GetAll()
		{
			var hash = await _featureToggleService.GetDepartmentFlagStateHashAsync(DepartmentId);
			var etag = "\"" + hash + "\"";
			Response.Headers["ETag"] = etag;

			var ifNoneMatch = Request.Headers["If-None-Match"].ToString();
			if (!string.IsNullOrEmpty(ifNoneMatch) && ifNoneMatch == etag)
				return StatusCode(StatusCodes.Status304NotModified);

			var evaluations = await _featureToggleService.EvaluateAllForDepartmentAsync(DepartmentId);

			var result = new FeatureTogglesResult { StateHash = hash };
			foreach (var evaluation in evaluations)
				result.Data.Add(MapEvaluation(evaluation));

			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>Evaluates a single flag for the caller's department.</summary>
		[HttpGet("Get")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize]
		public async Task<ActionResult<FeatureToggleResult>> Get(string key)
		{
			var evaluation = await _featureToggleService.EvaluateAsync(key, DepartmentId);

			var result = new FeatureToggleResult { Data = MapEvaluation(evaluation) };
			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		/// <summary>Lightweight enabled-only check for a single flag.</summary>
		[HttpGet("GetState")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize]
		public async Task<ActionResult<FeatureToggleResult>> GetState(string key)
		{
			var enabled = await _featureToggleService.IsEnabledAsync(key, DepartmentId);

			var result = new FeatureToggleResult { Data = new FeatureToggleData { Key = key, Enabled = enabled } };
			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		#endregion

		#region Flag management (SystemAdmin)

		[HttpGet("GetFlags")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.SystemAdmin)]
		public async Task<ActionResult<FeatureFlagsResult>> GetFlags(bool includeArchived = true)
		{
			var flags = await _featureToggleService.GetAllFlagsAsync(includeArchived, bypassCache: true);

			var result = new FeatureFlagsResult();
			foreach (var flag in flags)
				result.Data.Add(MapFlag(flag));

			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		[HttpGet("GetFlag")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.SystemAdmin)]
		public async Task<ActionResult<FeatureFlagResult>> GetFlag(string key)
		{
			var flag = await _featureToggleService.GetFlagByKeyAsync(key, bypassCache: true);

			var result = new FeatureFlagResult();
			if (flag == null)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return result;
			}

			result.Data = MapFlag(flag);
			result.PageSize = 1;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		[HttpPost("SaveFlag")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.SystemAdmin)]
		public async Task<ActionResult<FeatureFlagResult>> SaveFlag([FromBody] SaveFeatureFlagInput input)
		{
			if (input == null || string.IsNullOrWhiteSpace(input.Key) || string.IsNullOrWhiteSpace(input.Name))
				return BadRequest("Key and Name are required.");

			var existing = await _featureToggleService.GetFlagByKeyAsync(input.Key, bypassCache: true);
			var isNew = existing == null;
			var flag = existing ?? new FeatureFlag();

			flag.FlagKey = input.Key;
			flag.Name = input.Name;
			flag.Description = input.Description;
			flag.Category = input.Category;
			flag.Tags = input.Tags;
			flag.FlagType = input.FlagType;
			flag.IsEnabledGlobally = input.IsEnabledGlobally;
			flag.DefaultValue = input.DefaultValue;
			flag.OffValue = input.OffValue;
			flag.RolloutPercentage = input.RolloutPercentage;
			flag.MinimumPlanType = input.MinimumPlanType;
			flag.Environment = input.Environment;
			flag.EnableOn = input.EnableOn;
			flag.DisableOn = input.DisableOn;
			flag.IsArchived = input.IsArchived;
			flag.IsPermanent = input.IsPermanent;

			var saved = await _featureToggleService.SaveFlagAsync(flag, UserId);

			var result = new FeatureFlagResult { Data = MapFlag(saved) };
			result.PageSize = 1;
			result.Status = isNew ? ResponseHelper.Created : ResponseHelper.Updated;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		[HttpPost("ArchiveFlag")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.SystemAdmin)]
		public async Task<ActionResult<FeatureFlagResult>> ArchiveFlag(string key, bool archived = true)
		{
			var ok = await _featureToggleService.ArchiveFlagAsync(key, UserId, archived);

			var result = new FeatureFlagResult();
			if (!ok)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return result;
			}

			result.Data = MapFlag(await _featureToggleService.GetFlagByKeyAsync(key, bypassCache: true));
			result.PageSize = 1;
			result.Status = ResponseHelper.Updated;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		[HttpPost("SetGlobalEnabled")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.SystemAdmin)]
		public async Task<ActionResult<FeatureFlagResult>> SetGlobalEnabled([FromBody] SetGlobalEnabledInput input)
		{
			if (input == null || string.IsNullOrWhiteSpace(input.Key))
				return BadRequest("Key is required.");

			var saved = await _featureToggleService.SetGlobalEnabledAsync(input.Key, input.Enabled, UserId);

			var result = new FeatureFlagResult();
			if (saved == null)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return result;
			}

			result.Data = MapFlag(saved);
			result.PageSize = 1;
			result.Status = ResponseHelper.Updated;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		[HttpPost("SetRollout")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.SystemAdmin)]
		public async Task<ActionResult<FeatureFlagResult>> SetRollout([FromBody] SetRolloutInput input)
		{
			if (input == null || string.IsNullOrWhiteSpace(input.Key))
				return BadRequest("Key is required.");

			var saved = await _featureToggleService.SetRolloutPercentageAsync(input.Key, input.Percentage, UserId);

			var result = new FeatureFlagResult();
			if (saved == null)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return result;
			}

			result.Data = MapFlag(saved);
			result.PageSize = 1;
			result.Status = ResponseHelper.Updated;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		[HttpDelete("DeleteFlag")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.SystemAdmin)]
		public async Task<ActionResult<FeatureFlagResult>> DeleteFlag(string key)
		{
			var ok = await _featureToggleService.DeleteFlagAsync(key, UserId);

			var result = new FeatureFlagResult();
			if (!ok)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return result;
			}

			result.Status = ResponseHelper.Deleted;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		#endregion

		#region Targeting rules & prerequisites (SystemAdmin)

		[HttpGet("GetTargetingRules")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.SystemAdmin)]
		public async Task<ActionResult<FeatureFlagTargetingRulesResult>> GetTargetingRules(string key)
		{
			var rules = await _featureToggleService.GetTargetingRulesForFlagAsync(key);

			var result = new FeatureFlagTargetingRulesResult();
			foreach (var rule in rules)
				result.Data.Add(MapRule(rule));

			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		[HttpPost("SaveTargetingRule")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.SystemAdmin)]
		public async Task<ActionResult<FeatureFlagTargetingRulesResult>> SaveTargetingRule([FromBody] SaveTargetingRuleInput input)
		{
			if (input == null || string.IsNullOrWhiteSpace(input.Key))
				return BadRequest("Key is required.");

			var flag = await _featureToggleService.GetFlagByKeyAsync(input.Key, bypassCache: true);
			if (flag == null)
			{
				var notFound = new FeatureFlagTargetingRulesResult();
				ResponseHelper.PopulateV4ResponseNotFound(notFound);
				return notFound;
			}

			var rule = new FeatureFlagTargetingRule
			{
				FeatureFlagTargetingRuleId = input.FeatureFlagTargetingRuleId,
				FeatureFlagId = flag.FeatureFlagId,
				Priority = input.Priority,
				AttributeType = input.AttributeType,
				OperatorType = input.OperatorType,
				ComparisonValue = input.ComparisonValue,
				ResultEnabled = input.ResultEnabled,
				ResultValue = input.ResultValue,
				RolloutPercentage = input.RolloutPercentage
			};

			var saved = await _featureToggleService.SaveTargetingRuleAsync(rule, UserId);

			var result = new FeatureFlagTargetingRulesResult();
			result.Data.Add(MapRule(saved));
			result.PageSize = 1;
			result.Status = input.FeatureFlagTargetingRuleId == 0 ? ResponseHelper.Created : ResponseHelper.Updated;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		[HttpDelete("DeleteTargetingRule")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.SystemAdmin)]
		public async Task<ActionResult<FeatureFlagTargetingRulesResult>> DeleteTargetingRule(int id)
		{
			var ok = await _featureToggleService.RemoveTargetingRuleAsync(id, UserId);

			var result = new FeatureFlagTargetingRulesResult();
			if (!ok)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return result;
			}

			result.Status = ResponseHelper.Deleted;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		[HttpGet("GetPrerequisites")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.SystemAdmin)]
		public async Task<ActionResult<FeatureFlagPrerequisitesResult>> GetPrerequisites(string key)
		{
			var prereqs = await _featureToggleService.GetPrerequisitesForFlagAsync(key);

			var result = new FeatureFlagPrerequisitesResult();
			foreach (var prereq in prereqs)
				result.Data.Add(MapPrerequisite(prereq));

			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		[HttpPost("AddPrerequisite")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.SystemAdmin)]
		public async Task<ActionResult<FeatureFlagPrerequisitesResult>> AddPrerequisite([FromBody] AddPrerequisiteInput input)
		{
			if (input == null || string.IsNullOrWhiteSpace(input.Key) || string.IsNullOrWhiteSpace(input.RequiredKey))
				return BadRequest("Key and RequiredKey are required.");

			FeatureFlagPrerequisite saved;
			try
			{
				saved = await _featureToggleService.AddPrerequisiteAsync(input.Key, input.RequiredKey, input.RequiredValue, UserId);
			}
			catch (InvalidOperationException ex)
			{
				return BadRequest(ex.Message);
			}

			var result = new FeatureFlagPrerequisitesResult();
			result.Data.Add(MapPrerequisite(saved));
			result.PageSize = 1;
			result.Status = ResponseHelper.Created;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		[HttpDelete("DeletePrerequisite")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.SystemAdmin)]
		public async Task<ActionResult<FeatureFlagPrerequisitesResult>> DeletePrerequisite(int id)
		{
			var ok = await _featureToggleService.RemovePrerequisiteAsync(id, UserId);

			var result = new FeatureFlagPrerequisitesResult();
			if (!ok)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return result;
			}

			result.Status = ResponseHelper.Deleted;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		#endregion

		#region Override management (SystemAdmin or department admin for own department)

		[HttpGet("GetOverrides")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize]
		public async Task<ActionResult<FeatureFlagOverridesResult>> GetOverrides(int? departmentId = null)
		{
			if (!IsSystemAdmin && !IsDepartmentAdmin)
				return Unauthorized();

			var targetDepartmentId = ResolveTargetDepartmentId(departmentId);
			var overrides = await _featureToggleService.GetOverridesForDepartmentAsync(targetDepartmentId, bypassCache: true);

			var result = new FeatureFlagOverridesResult();
			foreach (var ovr in overrides)
				result.Data.Add(MapOverride(ovr));

			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		[HttpPost("SetOverride")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize]
		public async Task<ActionResult<FeatureFlagOverrideResult>> SetOverride([FromBody] SetFeatureFlagOverrideInput input)
		{
			if (input == null || string.IsNullOrWhiteSpace(input.Key))
				return BadRequest("Key is required.");
			if (!IsSystemAdmin && !IsDepartmentAdmin)
				return Unauthorized();

			var targetDepartmentId = ResolveTargetDepartmentId(input.DepartmentId);

			FeatureFlagOverride saved;
			try
			{
				saved = await _featureToggleService.SetDepartmentOverrideAsync(input.Key, targetDepartmentId, input.IsEnabled, input.Value, input.Reason, input.ExpiresOn, UserId);
			}
			catch (InvalidOperationException ex)
			{
				return BadRequest(ex.Message);
			}

			var result = new FeatureFlagOverrideResult { Data = MapOverride(saved) };
			result.PageSize = 1;
			result.Status = ResponseHelper.Updated;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		[HttpDelete("RemoveOverride")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize]
		public async Task<ActionResult<FeatureFlagOverrideResult>> RemoveOverride(string key, int? departmentId = null)
		{
			if (!IsSystemAdmin && !IsDepartmentAdmin)
				return Unauthorized();

			var targetDepartmentId = ResolveTargetDepartmentId(departmentId);
			var ok = await _featureToggleService.RemoveDepartmentOverrideAsync(key, targetDepartmentId, UserId);

			var result = new FeatureFlagOverrideResult();
			if (!ok)
			{
				ResponseHelper.PopulateV4ResponseNotFound(result);
				return result;
			}

			result.Status = ResponseHelper.Deleted;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		#endregion

		#region Analytics (SystemAdmin)

		[HttpGet("GetUsage")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.SystemAdmin)]
		public async Task<ActionResult<FeatureFlagUsageResult>> GetUsage(string key, DateTime? from = null, DateTime? to = null)
		{
			var fromDate = from ?? DateTime.UtcNow.AddDays(-30);
			var toDate = to ?? DateTime.UtcNow;

			var usages = await _featureToggleService.GetUsageForFlagAsync(key, fromDate, toDate);

			var result = new FeatureFlagUsageResult();
			foreach (var usage in usages)
				result.Data.Add(MapUsage(usage));

			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		[HttpGet("GetStaleFlags")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.SystemAdmin)]
		public async Task<ActionResult<FeatureFlagsResult>> GetStaleFlags(int? olderThanDays = null)
		{
			var flags = await _featureToggleService.GetStaleFlagsAsync(olderThanDays);

			var result = new FeatureFlagsResult();
			foreach (var flag in flags)
				result.Data.Add(MapFlag(flag));

			result.PageSize = result.Data.Count;
			result.Status = ResponseHelper.Success;
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		#endregion

		#region Helpers & mapping

		private int ResolveTargetDepartmentId(int? requested)
		{
			if (IsSystemAdmin && requested.HasValue && requested.Value > 0)
				return requested.Value;
			return DepartmentId;
		}

		private static FeatureToggleData MapEvaluation(FeatureFlagEvaluation evaluation)
		{
			return new FeatureToggleData
			{
				Key = evaluation.Key,
				Enabled = evaluation.IsEnabled,
				Value = evaluation.Value,
				ValueType = evaluation.ValueType.ToString(),
				Source = evaluation.Source.ToString(),
				MatchedRuleId = evaluation.MatchedRuleId
			};
		}

		private static FeatureFlagData MapFlag(FeatureFlag flag)
		{
			if (flag == null)
				return null;

			return new FeatureFlagData
			{
				FeatureFlagId = flag.FeatureFlagId.ToString(),
				Key = flag.FlagKey,
				Name = flag.Name,
				Description = flag.Description,
				Category = flag.Category,
				Tags = flag.Tags,
				FlagType = flag.FlagType,
				FlagTypeName = ((FeatureFlagValueTypes)flag.FlagType).ToString(),
				IsEnabledGlobally = flag.IsEnabledGlobally,
				DefaultValue = flag.DefaultValue,
				OffValue = flag.OffValue,
				RolloutPercentage = flag.RolloutPercentage,
				MinimumPlanType = flag.MinimumPlanType,
				Environment = flag.Environment,
				EnableOn = flag.EnableOn?.ToString("O"),
				DisableOn = flag.DisableOn?.ToString("O"),
				IsArchived = flag.IsArchived,
				IsPermanent = flag.IsPermanent,
				LastEvaluatedOn = flag.LastEvaluatedOn?.ToString("O"),
				CreatedOn = flag.CreatedOn.ToString("O"),
				UpdatedOn = flag.UpdatedOn?.ToString("O")
			};
		}

		private static FeatureFlagOverrideData MapOverride(FeatureFlagOverride ovr)
		{
			if (ovr == null)
				return null;

			return new FeatureFlagOverrideData
			{
				FeatureFlagOverrideId = ovr.FeatureFlagOverrideId.ToString(),
				FeatureFlagId = ovr.FeatureFlagId.ToString(),
				DepartmentId = ovr.DepartmentId,
				IsEnabled = ovr.IsEnabled,
				Value = ovr.FlagValue,
				Reason = ovr.Reason,
				ExpiresOn = ovr.ExpiresOn?.ToString("O")
			};
		}

		private static FeatureFlagTargetingRuleData MapRule(FeatureFlagTargetingRule rule)
		{
			if (rule == null)
				return null;

			return new FeatureFlagTargetingRuleData
			{
				FeatureFlagTargetingRuleId = rule.FeatureFlagTargetingRuleId.ToString(),
				FeatureFlagId = rule.FeatureFlagId.ToString(),
				Priority = rule.Priority,
				AttributeType = rule.AttributeType,
				OperatorType = rule.OperatorType,
				ComparisonValue = rule.ComparisonValue,
				ResultEnabled = rule.ResultEnabled,
				ResultValue = rule.ResultValue,
				RolloutPercentage = rule.RolloutPercentage
			};
		}

		private static FeatureFlagPrerequisiteData MapPrerequisite(FeatureFlagPrerequisite prereq)
		{
			if (prereq == null)
				return null;

			return new FeatureFlagPrerequisiteData
			{
				FeatureFlagPrerequisiteId = prereq.FeatureFlagPrerequisiteId.ToString(),
				FeatureFlagId = prereq.FeatureFlagId.ToString(),
				RequiredFeatureFlagId = prereq.RequiredFeatureFlagId.ToString(),
				RequiredValue = prereq.RequiredValue
			};
		}

		private static FeatureFlagUsageData MapUsage(FeatureFlagUsage usage)
		{
			return new FeatureFlagUsageData
			{
				FeatureFlagId = usage.FeatureFlagId.ToString(),
				DepartmentId = usage.DepartmentId,
				UsageDate = usage.UsageDate.ToString("O"),
				EvaluationCount = usage.EvaluationCount,
				EnabledCount = usage.EnabledCount,
				DisabledCount = usage.DisabledCount
			};
		}

		#endregion
	}
}
