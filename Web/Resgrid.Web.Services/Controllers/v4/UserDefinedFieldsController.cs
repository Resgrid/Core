using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.UserDefinedFields;
using Resgrid.Web.ServicesCore.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// User Defined Fields — allows departments to create custom fields for Calls,
	/// Personnel, Units and Contacts.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	public class UserDefinedFieldsController : V4AuthenticatedApiControllerbase
	{
		private readonly IUserDefinedFieldsService _udfService;
		private readonly IUdfRenderingService _renderingService;
		private readonly IEventAggregator _eventAggregator;

		public UserDefinedFieldsController(
			IUserDefinedFieldsService udfService,
			IUdfRenderingService renderingService,
			IEventAggregator eventAggregator)
		{
			_udfService = udfService;
			_renderingService = renderingService;
			_eventAggregator = eventAggregator;
		}

		/// <summary>Gets the active UDF definition and fields for the given entity type, filtered by the caller's role.</summary>
		[HttpGet("{entityType}")]
		[Authorize(Policy = ResgridResources.Udf_View)]
		[Produces(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<UdfDefinitionResult>> GetDefinition(int entityType, CancellationToken cancellationToken)
		{
			var definition = await _udfService.GetActiveDefinitionAsync(DepartmentId, entityType);

			var result = new UdfDefinitionResult { Data = null };

			if (definition != null)
			{
				bool isDeptAdmin = ClaimsAuthorizationHelper.IsUserDepartmentAdmin();
				bool isGroupAdmin = IsCallerGroupAdmin();
				var fields = await _udfService.GetVisibleFieldsForActiveDefinitionAsync(DepartmentId, entityType, isDeptAdmin, isGroupAdmin);
				result.Data = MapDefinitionToResult(definition, fields);
			}

			return Ok(result);
		}

		/// <summary>Creates or updates a UDF definition (triggers a new immutable version). Department admins only.</summary>
		[HttpPost]
		[Authorize(Policy = ResgridResources.Udf_Create)]
		[Consumes(MediaTypeNames.Application.Json)]
		[Produces(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		public async Task<ActionResult<UdfDefinitionResult>> SaveDefinition([FromBody] SaveUdfDefinitionInput input, CancellationToken cancellationToken)
		{
			if (!ModelState.IsValid)
				return BadRequest();

			var previousDefinition = await _udfService.GetActiveDefinitionAsync(DepartmentId, input.EntityType);
			var isNew = previousDefinition == null;

			var fields = input.Fields?.Select(f => new UdfField
			{
				UdfFieldId = f.UdfFieldId,
				Name = f.Name,
				Label = f.Label,
				Description = f.Description,
				Placeholder = f.Placeholder,
				FieldDataType = f.FieldDataType,
				IsRequired = f.IsRequired,
				IsReadOnly = f.IsReadOnly,
				DefaultValue = f.DefaultValue,
				ValidationRules = f.ValidationRules,
				SortOrder = f.SortOrder,
				GroupName = f.GroupName,
				IsVisibleOnMobile = f.IsVisibleOnMobile,
				IsVisibleOnReports = f.IsVisibleOnReports,
				IsEnabled = f.IsEnabled,
				Visibility = f.Visibility
			}).ToList() ?? new List<UdfField>();

			var saved = await _udfService.SaveDefinitionAsync(DepartmentId, input.EntityType, fields, UserId, cancellationToken);

			_eventAggregator.SendMessage<AuditEvent>(new AuditEvent
			{
				DepartmentId = DepartmentId,
				UserId = UserId,
				Type = isNew ? AuditLogTypes.UdfDefinitionCreated : AuditLogTypes.UdfDefinitionUpdated,
				Before = isNew ? null : JsonSerializer.Serialize(new { previousDefinition.UdfDefinitionId, previousDefinition.Version, previousDefinition.EntityType }),
				After = JsonSerializer.Serialize(new { saved.UdfDefinitionId, saved.Version, saved.EntityType }),
				Successful = true,
				IpAddress = IpAddressHelper.GetRequestIP(Request, true),
				ServerName = Environment.MachineName,
				UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}"
			});

			var savedFields = await _udfService.GetFieldsForActiveDefinitionAsync(DepartmentId, input.EntityType);
			return Ok(new UdfDefinitionResult { Data = MapDefinitionToResult(saved, savedFields) });
		}

		/// <summary>Gets all UDF field values for a specific entity, restricted to fields visible to the caller.</summary>
		[HttpGet("Values/{entityType}/{entityId}")]
		[Authorize(Policy = ResgridResources.Udf_View)]
		[Produces(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<UdfFieldValuesResult>> GetFieldValues(int entityType, string entityId, CancellationToken cancellationToken)
		{
			bool isDeptAdmin = ClaimsAuthorizationHelper.IsUserDepartmentAdmin();
			bool isGroupAdmin = IsCallerGroupAdmin();
			var visibleFields = await _udfService.GetVisibleFieldsForActiveDefinitionAsync(DepartmentId, entityType, isDeptAdmin, isGroupAdmin);
			var visibleFieldIds = visibleFields.Select(f => f.UdfFieldId).ToHashSet();

			var values = await _udfService.GetFieldValuesForEntityAsync(DepartmentId, entityType, entityId);

			return Ok(new UdfFieldValuesResult
			{
				Data = values
					.Where(v => visibleFieldIds.Contains(v.UdfFieldId))
					.Select(v => new UdfFieldValueResultData
					{
						UdfFieldValueId = v.UdfFieldValueId,
						UdfFieldId = v.UdfFieldId,
						UdfDefinitionId = v.UdfDefinitionId,
						EntityId = v.EntityId,
						EntityType = v.EntityType,
						Value = v.Value
					}).ToList()
			});
		}

		/// <summary>Saves UDF field values for a specific entity (validates server-side). Only values for fields visible to the caller are accepted.</summary>
		[HttpPost("Values")]
		[Authorize(Policy = ResgridResources.Udf_Update)]
		[Consumes(MediaTypeNames.Application.Json)]
		[Produces(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		public async Task<ActionResult> SaveFieldValues([FromBody] SaveUdfFieldValuesInput input, CancellationToken cancellationToken)
		{
			if (!ModelState.IsValid)
				return BadRequest();

			bool isDeptAdmin = ClaimsAuthorizationHelper.IsUserDepartmentAdmin();
			bool isGroupAdmin = IsCallerGroupAdmin();
			var visibleFields = await _udfService.GetVisibleFieldsForActiveDefinitionAsync(DepartmentId, input.EntityType, isDeptAdmin, isGroupAdmin);
			var visibleFieldIds = visibleFields.Select(f => f.UdfFieldId).ToHashSet();

			// Only accept values for fields the caller can see — silently discard the rest.
			var values = input.Values?
				.Where(v => visibleFieldIds.Contains(v.UdfFieldId))
				.Select(v => new UdfFieldValue
				{
					UdfFieldId = v.UdfFieldId,
					Value = v.Value
				}).ToList() ?? new List<UdfFieldValue>();

			var errors = await _udfService.SaveFieldValuesForEntityAsync(
				DepartmentId, input.EntityType, input.EntityId, values, UserId, cancellationToken);

			if (errors.Count > 0)
				return BadRequest(errors);

			_eventAggregator.SendMessage<AuditEvent>(new AuditEvent
			{
				DepartmentId = DepartmentId,
				UserId = UserId,
				Type = AuditLogTypes.UdfFieldValueSaved,
				After = JsonSerializer.Serialize(new { input.EntityType, input.EntityId }),
				Successful = true,
				IpAddress = IpAddressHelper.GetRequestIP(Request, true),
				ServerName = Environment.MachineName,
				UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}"
			});

			return Ok();
		}

		/// <summary>Removes a field from the active definition (creates a new version without it). Department admins only.</summary>
		[HttpDelete("Fields/{fieldId}")]
		[Authorize(Policy = ResgridResources.Udf_Delete)]
		[Produces(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		public async Task<ActionResult<UdfDefinitionResult>> DeleteField(string fieldId, CancellationToken cancellationToken)
		{
			var newDefinition = await _udfService.DeleteFieldFromDefinitionAsync(fieldId, DepartmentId, UserId, cancellationToken);

			if (newDefinition == null)
				return NotFound();

			_eventAggregator.SendMessage<AuditEvent>(new AuditEvent
			{
				DepartmentId = DepartmentId,
				UserId = UserId,
				Type = AuditLogTypes.UdfFieldRemoved,
				Before = JsonSerializer.Serialize(new { UdfFieldId = fieldId }),
				After = JsonSerializer.Serialize(new { newDefinition.UdfDefinitionId, newDefinition.Version }),
				Successful = true,
				IpAddress = IpAddressHelper.GetRequestIP(Request, true),
				ServerName = Environment.MachineName,
				UserAgent = $"{Request.Headers["User-Agent"]} {Request.Headers["Accept-Language"]}"
			});

			var fields = await _udfService.GetFieldsForActiveDefinitionAsync(DepartmentId, newDefinition.EntityType);
			return Ok(new UdfDefinitionResult { Data = MapDefinitionToResult(newDefinition, fields) });
		}

		/// <summary>Returns the React Native JSON schema for the active UDF definition (empty form), filtered by caller role.</summary>
		[HttpGet("Schema/{entityType}")]
		[Authorize(Policy = ResgridResources.Udf_View)]
		[Produces(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<UdfSchemaResult>> GetSchema(int entityType, CancellationToken cancellationToken)
		{
			var definition = await _udfService.GetActiveDefinitionAsync(DepartmentId, entityType);
			bool isDeptAdmin = ClaimsAuthorizationHelper.IsUserDepartmentAdmin();
			bool isGroupAdmin = IsCallerGroupAdmin();
			var fields = definition != null
				? await _udfService.GetVisibleFieldsForActiveDefinitionAsync(DepartmentId, entityType, isDeptAdmin, isGroupAdmin)
				: new List<UdfField>();

			var schema = _renderingService.GenerateReactNativeSchema(definition, fields, new List<UdfFieldValue>());
			return Ok(new UdfSchemaResult { Data = schema });
		}

		/// <summary>Returns the React Native JSON schema pre-populated with an entity's existing values, filtered by caller role.</summary>
		[HttpGet("Schema/{entityType}/{entityId}")]
		[Authorize(Policy = ResgridResources.Udf_View)]
		[Produces(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		public async Task<ActionResult<UdfSchemaResult>> GetSchemaForEntity(int entityType, string entityId, CancellationToken cancellationToken)
		{
			var definition = await _udfService.GetActiveDefinitionAsync(DepartmentId, entityType);
			bool isDeptAdmin = ClaimsAuthorizationHelper.IsUserDepartmentAdmin();
			bool isGroupAdmin = IsCallerGroupAdmin();
			var fields = definition != null
				? await _udfService.GetVisibleFieldsForActiveDefinitionAsync(DepartmentId, entityType, isDeptAdmin, isGroupAdmin)
				: new List<UdfField>();

			var visibleFieldIds = fields.Select(f => f.UdfFieldId).ToHashSet();
			var allValues = definition != null
				? await _udfService.GetFieldValuesForEntityAsync(DepartmentId, entityType, entityId)
				: new List<UdfFieldValue>();
			var values = allValues.Where(v => visibleFieldIds.Contains(v.UdfFieldId)).ToList();

			var schema = _renderingService.GenerateReactNativeSchema(definition, fields, values);
			return Ok(new UdfSchemaResult { Data = schema });
		}

		// ── Private helpers ──────────────────────────────────────────────────────

		/// <summary>
		/// Returns true if the current caller holds a group-admin claim for any group.
		/// </summary>
		private bool IsCallerGroupAdmin()
		{
			return HttpContext.User.Claims
				.Any(c => c.Type.StartsWith(ResgridClaimTypes.Resources.Group + "/", StringComparison.Ordinal)
					&& c.Value == ResgridClaimTypes.Actions.Update);
		}

		private static UdfDefinitionResultData MapDefinitionToResult(UdfDefinition definition, List<UdfField> fields)
		{
			return new UdfDefinitionResultData
			{
				UdfDefinitionId = definition.UdfDefinitionId,
				DepartmentId = definition.DepartmentId,
				EntityType = definition.EntityType,
				Version = definition.Version,
				IsActive = definition.IsActive,
				Fields = fields?.Select(f => new UdfFieldResultData
				{
					UdfFieldId = f.UdfFieldId,
					UdfDefinitionId = f.UdfDefinitionId,
					Name = f.Name,
					Label = f.Label,
					Description = f.Description,
					Placeholder = f.Placeholder,
					FieldDataType = f.FieldDataType,
					IsRequired = f.IsRequired,
					IsReadOnly = f.IsReadOnly,
					DefaultValue = f.DefaultValue,
					ValidationRules = f.ValidationRules,
					SortOrder = f.SortOrder,
					GroupName = f.GroupName,
					IsVisibleOnMobile = f.IsVisibleOnMobile,
					IsVisibleOnReports = f.IsVisibleOnReports,
					IsEnabled = f.IsEnabled,
					Visibility = f.Visibility
				}).ToList() ?? new List<UdfFieldResultData>()
			};
		}
	}
}

