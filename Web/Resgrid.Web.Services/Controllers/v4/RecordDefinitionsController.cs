using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Providers.Claims;
using Resgrid.Web.Services.Helpers;
using Resgrid.Web.Services.Models.v4.Records;
using Resgrid.Web.ServicesCore.Helpers;

namespace Resgrid.Web.Services.Controllers.v4
{
	/// <summary>
	/// Definition management and runtime catalog over v4 (RMS plan section 5.4, RMS-1B): template browse, clone/new
	/// draft, ETag-guarded draft saves, validation, impact preview, publish, retire, history, safe diff and draft
	/// migration. Reads need Record_View; authoring needs RecordDefinition_Update; publish/retire RecordDefinition_Publish.
	/// Locked system definitions are listed but never editable here.
	/// </summary>
	[Route("api/v{VersionId:apiVersion}/[controller]")]
	[ApiVersion("4.0")]
	[ApiExplorerSettings(GroupName = "v4")]
	[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
	public class RecordDefinitionsController : V4AuthenticatedApiControllerbase
	{
		private readonly IRecordDefinitionsService _definitions;
		private readonly IRecordTemplatePacksService _templates;
		private readonly IRecordsCutoverService _cutoverService;
		private readonly IRecordsPrintLayoutService _printLayouts;

		public RecordDefinitionsController(IRecordDefinitionsService definitions, IRecordTemplatePacksService templates, IRecordsCutoverService cutoverService, IRecordsPrintLayoutService printLayouts)
		{
			_printLayouts = printLayouts;
			_definitions = definitions;
			_templates = templates;
			_cutoverService = cutoverService;
		}

		[HttpGet("List")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<RecordDefinitionsResult>> List(bool includeRetired = false)
		{
			if (!await FlagOnAsync()) return NotFound();
			var result = new RecordDefinitionsResult { Data = await _definitions.ListAsync(DepartmentId, includeRetired), Status = ResponseHelper.Success };
			result.PageSize = result.Data.Count;
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>Published department definitions with their schemas: what a client offers on New Record and renders against.</summary>
		[HttpGet("Published")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<RecordDefinitionVersionsResult>> Published()
		{
			if (!await FlagOnAsync()) return NotFound();
			var result = new RecordDefinitionVersionsResult { Data = (await _definitions.GetPublishedAsync(DepartmentId)).Select(RecordsRms1bApiMapper.ToVersion).ToList(), Status = ResponseHelper.Success };
			result.PageSize = result.Data.Count;
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		[HttpGet("Get")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<RecordDefinitionResult>> Get(string key)
		{
			if (!await FlagOnAsync()) return NotFound();
			var aggregate = await _definitions.GetAsync(DepartmentId, key);
			if (aggregate == null) return NotFound();
			return Ok(Wrap(aggregate));
		}

		[HttpGet("Version")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<RecordDefinitionVersionResult>> Version(string key, int version)
		{
			if (!await FlagOnAsync()) return NotFound();
			var row = await _definitions.GetVersionAsync(DepartmentId, key, version);
			if (row == null) return NotFound();
			return Ok(Wrap(row));
		}

		[HttpGet("Templates")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<RecordTemplatePacksResult>> Templates()
		{
			if (!await FlagOnAsync()) return NotFound();
			var result = new RecordTemplatePacksResult { Data = await _templates.GetCatalogAsync(), Status = ResponseHelper.Success };
			result.PageSize = result.Data.Count;
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		[HttpGet("Profiles")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<RecordJurisdictionProfilesResult>> Profiles()
		{
			if (!await FlagOnAsync()) return NotFound();
			var result = new RecordJurisdictionProfilesResult { Data = await _templates.GetProfilesAsync(), Status = ResponseHelper.Success };
			result.PageSize = result.Data.Count;
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		/// <summary>A product template rendered for a profile and locale: what a clone would start from, with its provenance statement.</summary>
		[HttpGet("Template")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<RecordTemplateRenderingResult>> Template(string key, string profile = "generic", string locale = null)
		{
			if (!await FlagOnAsync()) return NotFound();
			try
			{
				var rendering = await _templates.RenderAsync(key, profile, locale);
				if (rendering == null) return NotFound();
				var result = new RecordTemplateRenderingResult { Data = RecordsRms1bApiMapper.ToRendering(rendering), Status = ResponseHelper.Success, PageSize = 1 };
				ResponseHelper.PopulateV4ResponseData(result);
				return Ok(result);
			}
			catch (ArgumentException ex) { return Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message, type: "record_definition_validation"); }
		}

		[HttpPost("Create")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status201Created)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[Authorize(Policy = ResgridResources.RecordDefinition_Update)]
		public async Task<ActionResult<RecordDefinitionResult>> Create([FromBody] CreateRecordDefinitionInput input, CancellationToken cancellationToken)
		{
			if (input == null) return BadRequest();
			var usable = await UsableAsync();
			if (usable != null) return usable;
			try
			{
				var aggregate = await _definitions.CreateAsync(DepartmentId, UserId, new RecordDefinitionCreateInput
				{
					DefinitionKey = input.DefinitionKey, Name = input.Name, Category = input.Category, TemplateKey = input.TemplateKey, CloneFromDefinitionKey = input.CloneFromDefinitionKey, JurisdictionProfileKey = input.JurisdictionProfileKey, Locale = input.Locale
				}, cancellationToken);
				return StatusCode(StatusCodes.Status201Created, Wrap(aggregate, ResponseHelper.Created));
			}
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpPost("OpenDraft")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.RecordDefinition_Update)]
		public async Task<ActionResult<RecordDefinitionVersionResult>> OpenDraft(string key, CancellationToken cancellationToken)
		{
			var usable = await UsableAsync();
			if (usable != null) return usable;
			try { return Ok(Wrap(await _definitions.OpenDraftAsync(DepartmentId, UserId, key, cancellationToken))); }
			catch (Exception ex) { return Fail(ex); }
		}

		/// <summary>ETag-guarded save of a draft version (send the version's RowVersion). A published version refuses edits.</summary>
		[HttpPost("SaveDraft")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status400BadRequest)]
		[ProducesResponseType(StatusCodes.Status409Conflict)]
		[Authorize(Policy = ResgridResources.RecordDefinition_Update)]
		public async Task<ActionResult<RecordDefinitionVersionResult>> SaveDraft(string key, int version, [FromBody] SaveRecordDefinitionDraftInput input, CancellationToken cancellationToken)
		{
			if (input?.Draft == null) return BadRequest();
			var usable = await UsableAsync();
			if (usable != null) return usable;
			var rowVersion = RecordsApiContract.ParseETag(Request.Headers[RecordsApiContract.IfMatchHeader]) ?? input.RowVersion;
			try { return Ok(Wrap(await _definitions.SaveDraftAsync(DepartmentId, UserId, key, version, rowVersion, input.Draft, cancellationToken))); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpPost("Validate")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.RecordDefinition_Update)]
		public async Task<ActionResult<RecordDefinitionValidationResult>> Validate([FromBody] RecordDefinitionDraftInput input)
		{
			if (input == null) return BadRequest();
			if (!await FlagOnAsync()) return NotFound();
			var result = new RecordDefinitionValidationResult { Data = await _definitions.ValidateAsync(DepartmentId, input), Status = ResponseHelper.Success, PageSize = 1 };
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		[HttpGet("Impact")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.RecordDefinition_Update)]
		public async Task<ActionResult<RecordDefinitionImpactResult>> Impact(string key, int version)
		{
			if (!await FlagOnAsync()) return NotFound();
			try
			{
				var result = new RecordDefinitionImpactResult { Data = await _definitions.ImpactPreviewAsync(DepartmentId, key, version), Status = ResponseHelper.Success, PageSize = 1 };
				ResponseHelper.PopulateV4ResponseData(result);
				return Ok(result);
			}
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpPost("Publish")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status409Conflict)]
		[Authorize(Policy = ResgridResources.RecordDefinition_Publish)]
		public async Task<ActionResult<RecordDefinitionVersionResult>> Publish(string key, int version, [FromBody] PublishRecordDefinitionInput input, CancellationToken cancellationToken)
		{
			var usable = await UsableAsync();
			if (usable != null) return usable;
			var rowVersion = RecordsApiContract.ParseETag(Request.Headers[RecordsApiContract.IfMatchHeader]) ?? input?.RowVersion ?? 0;
			try { return Ok(Wrap(await _definitions.PublishAsync(DepartmentId, UserId, key, version, rowVersion, cancellationToken))); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpPost("Retire")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.RecordDefinition_Publish)]
		public async Task<ActionResult<RecordDefinitionResult>> Retire(string key, [FromBody] RetireRecordDefinitionInput input, CancellationToken cancellationToken)
		{
			if (input == null) return BadRequest();
			var usable = await UsableAsync();
			if (usable != null) return usable;
			var rowVersion = RecordsApiContract.ParseETag(Request.Headers[RecordsApiContract.IfMatchHeader]) ?? input.RowVersion;
			try
			{
				await _definitions.RetireAsync(DepartmentId, UserId, key, rowVersion, input.Reason, cancellationToken);
				return Ok(Wrap(await _definitions.GetAsync(DepartmentId, key)));
			}
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpDelete("DeleteDraft")]
		[ProducesResponseType(StatusCodes.Status204NoContent)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.RecordDefinition_Update)]
		public async Task<IActionResult> DeleteDraft(string key, int version, CancellationToken cancellationToken)
		{
			var usable = await UsableAsync();
			if (usable != null) return usable;
			try { return await _definitions.DeleteDraftAsync(DepartmentId, UserId, key, version, cancellationToken) ? NoContent() : NotFound(); }
			catch (Exception ex) { return Fail(ex); }
		}

		[HttpGet("History")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<RecordDefinitionVersionsResult>> History(string key)
		{
			if (!await FlagOnAsync()) return NotFound();
			var result = new RecordDefinitionVersionsResult { Data = (await _definitions.HistoryAsync(DepartmentId, key)).Select(RecordsRms1bApiMapper.ToVersion).ToList(), Status = ResponseHelper.Success };
			result.PageSize = result.Data.Count;
			ResponseHelper.PopulateV4ResponseData(result);
			return Ok(result);
		}

		[HttpGet("Diff")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<RecordDefinitionDiffResult>> Diff(string key, int from, int to)
		{
			if (!await FlagOnAsync()) return NotFound();
			try
			{
				var result = new RecordDefinitionDiffResult { Data = await _definitions.DiffAsync(DepartmentId, key, from, to), Status = ResponseHelper.Success, PageSize = 1 };
				ResponseHelper.PopulateV4ResponseData(result);
				return Ok(result);
			}
			catch (Exception ex) { return Fail(ex); }
		}

		/// <summary>Migrates compatible drafts to a newer version through an explicit mapping (Preview = true only counts). Finalized Records never move.</summary>
		[HttpPost("MigrateDrafts")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.RecordDefinition_Update)]
		public async Task<ActionResult<RecordDefinitionMigrationApiResult>> MigrateDrafts(string key, [FromBody] MigrateRecordDraftsInput input, CancellationToken cancellationToken)
		{
			if (input == null) return BadRequest();
			var usable = await UsableAsync();
			if (usable != null) return usable;
			try
			{
				var result = new RecordDefinitionMigrationApiResult { Data = await _definitions.MigrateDraftsAsync(DepartmentId, UserId, key, input.FromVersion, input.ToVersion, input.Mapping, input.Preview, cancellationToken), Status = ResponseHelper.Success, PageSize = 1 };
				ResponseHelper.PopulateV4ResponseData(result);
				return Ok(result);
			}
			catch (Exception ex) { return Fail(ex); }
		}

		/// <summary>
		/// The print layout that applies to one definition version (RMS plan section 4.10.1): the definition-scope config
		/// when one is saved and applies, plus the branding block it resolves to and the composite layout version that
		/// the provenance footer stamps. Locked system definitions resolve to the department default only.
		/// </summary>
		[HttpGet("Layout")]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[ProducesResponseType(StatusCodes.Status404NotFound)]
		[Authorize(Policy = ResgridResources.Record_View)]
		public async Task<ActionResult<RecordDefinitionLayoutResult>> Layout(string key, int? version = null)
		{
			if (!await FlagOnAsync()) return NotFound();
			var aggregate = await _definitions.GetAsync(DepartmentId, key);
			if (aggregate == null) return NotFound();
			var target = version.HasValue ? aggregate.Versions.FirstOrDefault(v => v.Version == version.Value) : aggregate.Published ?? aggregate.Latest ?? aggregate.Draft;
			if (target == null) return NotFound();
			var stored = await _printLayouts.GetDefinitionLayoutAsync(DepartmentId, aggregate.Definition.DefinitionKey);
			return Ok(WrapLayout(aggregate.Definition.DefinitionKey, target.Version, stored, await _printLayouts.ResolveForDefinitionAsync(DepartmentId, aggregate.Definition.DefinitionKey, target.Version)));
		}

		/// <summary>Saves the definition-scope print layout; every save is a new layout version the footer names.</summary>
		[HttpPost("Layout")]
		[Consumes(MediaTypeNames.Application.Json)]
		[ProducesResponseType(StatusCodes.Status200OK)]
		[Authorize(Policy = ResgridResources.RecordDefinition_Update)]
		public async Task<ActionResult<RecordDefinitionLayoutResult>> SaveLayout(string key, [FromBody] RecordsDefinitionLayoutConfig input, CancellationToken cancellationToken)
		{
			if (input == null) return BadRequest();
			var usable = await UsableAsync();
			if (usable != null) return usable;
			var aggregate = await _definitions.GetAsync(DepartmentId, key);
			if (aggregate == null || aggregate.Definition.Owner == (int)RmsDefinitionOwner.System) return NotFound();
			try
			{
				var stored = await _printLayouts.SaveDefinitionLayoutAsync(DepartmentId, UserId, aggregate.Definition.DefinitionKey, input, cancellationToken);
				var target = aggregate.Published ?? aggregate.Latest ?? aggregate.Draft;
				return Ok(WrapLayout(aggregate.Definition.DefinitionKey, target?.Version ?? 0, stored, await _printLayouts.ResolveForDefinitionAsync(DepartmentId, aggregate.Definition.DefinitionKey, target?.Version ?? 0)));
			}
			catch (Exception ex) { return Fail(ex); }
		}

		private RecordDefinitionLayoutResult WrapLayout(string definitionKey, int definitionVersion, RmsRecordPrintLayout stored, RecordsResolvedPrintLayout resolved)
		{
			var result = new RecordDefinitionLayoutResult
			{
				Data = new RecordDefinitionLayoutData
				{
					DefinitionKey = definitionKey, DefinitionVersion = definitionVersion,
					StoredLayoutVersion = stored?.Version > 0 ? stored.LayoutVersion : null, Config = stored?.DefinitionConfig,
					AppliesToVersion = resolved.Definition != null, ResolvedLayoutVersion = resolved.LayoutVersion, Branding = resolved.Branding
				},
				Status = ResponseHelper.Success, PageSize = 1
			};
			ResponseHelper.PopulateV4ResponseData(result);
			return result;
		}

		private RecordDefinitionResult Wrap(RecordDefinitionAggregate aggregate, string status = ResponseHelper.Success)
		{
			var result = new RecordDefinitionResult { Data = RecordsRms1bApiMapper.ToDefinition(aggregate), Status = status, PageSize = 1 };
			ResponseHelper.PopulateV4ResponseData(result);
			Response.Headers[RecordsApiContract.ETagHeader] = result.Data.ETag;
			return result;
		}

		private RecordDefinitionVersionResult Wrap(RmsRecordDefinitionVersion version)
		{
			var result = new RecordDefinitionVersionResult { Data = RecordsRms1bApiMapper.ToVersion(version), Status = ResponseHelper.Success, PageSize = 1 };
			ResponseHelper.PopulateV4ResponseData(result);
			Response.Headers[RecordsApiContract.ETagHeader] = result.Data.ETag;
			return result;
		}

		private ActionResult Fail(Exception ex)
		{
			switch (ex)
			{
				case UnauthorizedAccessException _: return Forbid();
				case RecordConcurrencyException conflict: return Problem(statusCode: StatusCodes.Status409Conflict, title: conflict.Message, type: "record_definition_conflict");
				case ArgumentException argument: return Problem(statusCode: StatusCodes.Status400BadRequest, title: argument.Message, type: "record_definition_validation");
				case InvalidOperationException invalid: return Problem(statusCode: StatusCodes.Status409Conflict, title: invalid.Message, type: "record_definition_state");
				default: throw ex;
			}
		}

		private async Task<bool> FlagOnAsync() => (await _cutoverService.GetModuleStateAsync(DepartmentId)).FlagEnabled;

		private async Task<ActionResult> UsableAsync()
		{
			var state = await _cutoverService.GetModuleStateAsync(DepartmentId);
			if (!state.FlagEnabled) return NotFound();
			return state.RecordsUsable ? null : Problem(statusCode: StatusCodes.Status409Conflict, title: "Records is not activated for this department.", type: "records_not_activated");
		}
	}
}
