using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc.Rendering;
using Resgrid.Model;

namespace Resgrid.Web.Areas.User.Models.Records
{
	// RMS-1B definition designer, saved reports, definition-driven record form; RMS-1C template packs and deployments.

	public class RecordDefinitionsIndexView : RecordsBaseView
	{
		public RecordsModuleState ModuleState { get; set; }
		public Department Department { get; set; }
		public List<RecordDefinitionSummary> Definitions { get; set; } = new List<RecordDefinitionSummary>();
		public bool CanPublish { get; set; }
		public bool IncludeRetired { get; set; }
	}

	public class RecordTemplatesView : RecordsBaseView
	{
		public List<RecordTemplatePackSummary> Packs { get; set; } = new List<RecordTemplatePackSummary>();
		public List<RmsJurisdictionProfileVersion> Profiles { get; set; } = new List<RmsJurisdictionProfileVersion>();
	}

	public class RecordDefinitionCreateView : RecordsBaseView
	{
		public string DefinitionKey { get; set; }
		public string Name { get; set; }
		public string Category { get; set; }
		public string TemplateKey { get; set; }
		public string CloneFromDefinitionKey { get; set; }
		public string JurisdictionProfileKey { get; set; } = "generic";
		public string Locale { get; set; }
		[Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
		public RecordTemplateRendering Rendering { get; set; }
		[Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
		public List<SelectListItem> Profiles { get; set; } = new List<SelectListItem>();
		[Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
		public List<SelectListItem> Locales { get; set; } = new List<SelectListItem>();
		[Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
		public List<SelectListItem> Templates { get; set; } = new List<SelectListItem>();
		[Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
		public List<SelectListItem> DepartmentDefinitions { get; set; } = new List<SelectListItem>();
	}

	/// <summary>The controlled designer: policies as form fields, the schema as a validated JSON document, and a read-only field table rendered from it.</summary>
	public class RecordDefinitionEditView : RecordsBaseView
	{
		public string DefinitionKey { get; set; }
		public int Version { get; set; }
		public long RowVersion { get; set; }
		public string Name { get; set; }
		public string Category { get; set; }
		public string Description { get; set; }
		public string PermittedSubjectTypes { get; set; }
		public int LifecyclePreset { get; set; } = (int)RmsLifecyclePreset.QuickEntry;
		public int Cardinality { get; set; } = (int)RmsRecordCardinality.MultiplePerCall;
		public List<int> ReviewerRoleIds { get; set; } = new List<int>();
		public List<int> ApproverRoleIds { get; set; } = new List<int>();
		public int? ReviewDueHours { get; set; }
		public int? ApproveDueHours { get; set; }
		public bool RequireAuthorAttestation { get; set; }
		public string NumberPrefix { get; set; }
		public int NumberAssignment { get; set; } = (int)RmsNumberAssignment.OnFinalize;
		public bool PerGroupSequence { get; set; }
		public bool PerIncidentSequence { get; set; }
		public bool ResetYearly { get; set; } = true;
		public int SequenceWidth { get; set; } = 4;
		public int? RetentionYears { get; set; }
		public int Classification { get; set; }
		public bool SurfaceResponder { get; set; }
		public bool SurfaceUnit { get; set; }
		public bool SurfaceIncidentCommand { get; set; }
		public bool SurfaceDispatch { get; set; }
		public bool AllowOffline { get; set; }
		public bool AllowAttachments { get; set; } = true;
		/// <summary>Media capture hygiene (RMS-1D): keep photo coordinates on this definition's attachments.</summary>
		public bool RetainMediaLocation { get; set; }
		public string SchemaJson { get; set; }
		public string MigrationMapJson { get; set; }
		public string ChangeNotes { get; set; }

		[Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
		public RecordDefinitionAggregate Aggregate { get; set; }
		[Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
		public RmsRecordDefinitionVersion VersionRow { get; set; }
		[Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
		public RecordDefinitionSchema Schema { get; set; }
		[Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
		public List<RecordDefinitionIssue> Issues { get; set; } = new List<RecordDefinitionIssue>();
		[Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
		public List<SelectListItem> Roles { get; set; } = new List<SelectListItem>();
		[Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
		public string MinimumClientCapability { get; set; }
		[Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
		public bool CanPublish { get; set; }
		[Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
		public bool IsPublished { get; set; }
		[Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
		public RecordDefinitionDiff TemplateDiff { get; set; }
		public bool IsDraft => VersionRow == null || VersionRow.IsDraft;

		public RecordDefinitionDraftInput ToDraftInput()
		{
			return new RecordDefinitionDraftInput
			{
				Name = Name, Category = Category, Description = Description, PermittedSubjectTypes = PermittedSubjectTypes,
				LifecyclePreset = (RmsLifecyclePreset)LifecyclePreset, Cardinality = (RmsRecordCardinality)Cardinality,
				ReviewerRoleIds = ReviewerRoleIds ?? new List<int>(), ApproverRoleIds = ApproverRoleIds ?? new List<int>(),
				ReviewDueHours = ReviewDueHours, ApproveDueHours = ApproveDueHours, RequireAuthorAttestation = RequireAuthorAttestation,
				Numbering = new RecordDefinitionNumbering { Prefix = NumberPrefix?.Trim().ToUpperInvariant(), Assignment = (RmsNumberAssignment)NumberAssignment, PerGroupSequence = PerGroupSequence, PerIncidentSequence = PerIncidentSequence, ResetYearly = ResetYearly, SequenceWidth = SequenceWidth },
				RetentionYears = RetentionYears, Classification = (RmsFieldClassification)Classification,
				Schema = RecordDefinitionSchema.Parse(SchemaJson),
				ClientSurface = new RecordDefinitionClientSurface { Responder = SurfaceResponder, Unit = SurfaceUnit, IncidentCommand = SurfaceIncidentCommand, Dispatch = SurfaceDispatch, AllowOffline = AllowOffline, AllowAttachments = AllowAttachments, RetainMediaLocation = RetainMediaLocation },
				MigrationMap = string.IsNullOrWhiteSpace(MigrationMapJson) ? new List<RecordDefinitionFieldMapping>() : Newtonsoft.Json.JsonConvert.DeserializeObject<List<RecordDefinitionFieldMapping>>(MigrationMapJson) ?? new List<RecordDefinitionFieldMapping>(),
				ChangeNotes = ChangeNotes
			};
		}

		public static RecordDefinitionEditView From(RecordDefinitionAggregate aggregate, RmsRecordDefinitionVersion version)
		{
			var numbering = version.Numbering; var surface = version.ClientSurface;
			return new RecordDefinitionEditView
			{
				Aggregate = aggregate, VersionRow = version, DefinitionKey = aggregate.Definition.DefinitionKey, Version = version.Version, RowVersion = version.RowVersion,
				Name = aggregate.Definition.Name, Category = aggregate.Definition.Category, Description = aggregate.Definition.Description, PermittedSubjectTypes = aggregate.Definition.PermittedSubjectTypes,
				LifecyclePreset = version.LifecyclePreset, Cardinality = version.Cardinality,
				ReviewerRoleIds = Resgrid.Services.Records.RecordDefinitionsService.ParseIds(version.ReviewerRoleIds), ApproverRoleIds = Resgrid.Services.Records.RecordDefinitionsService.ParseIds(version.ApproverRoleIds),
				ReviewDueHours = version.ReviewDueHours, ApproveDueHours = version.ApproveDueHours, RequireAuthorAttestation = version.RequireAuthorAttestation,
				NumberPrefix = numbering.Prefix, NumberAssignment = (int)numbering.Assignment, PerGroupSequence = numbering.PerGroupSequence, PerIncidentSequence = numbering.PerIncidentSequence, ResetYearly = numbering.ResetYearly, SequenceWidth = numbering.SequenceWidth,
				RetentionYears = version.RetentionYears, Classification = version.Classification,
				SurfaceResponder = surface.Responder, SurfaceUnit = surface.Unit, SurfaceIncidentCommand = surface.IncidentCommand, SurfaceDispatch = surface.Dispatch, AllowOffline = surface.AllowOffline, AllowAttachments = surface.AllowAttachments, RetainMediaLocation = surface.RetainMediaLocation,
				SchemaJson = Newtonsoft.Json.JsonConvert.SerializeObject(version.Schema, Newtonsoft.Json.Formatting.Indented, new Newtonsoft.Json.JsonSerializerSettings { NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore, DefaultValueHandling = Newtonsoft.Json.DefaultValueHandling.Ignore }),
				MigrationMapJson = version.MigrationMapJson, ChangeNotes = version.ChangeNotes, Schema = version.Schema, IsPublished = version.IsPublished,
				MinimumClientCapability = version.MinimumClientCapability ?? RecordsClientCapabilities.Derive(version.Schema)
			};
		}
	}

	/// <summary>
	/// Definition-scope print layout (RMS plan section 4.10.1): section order, headings, hidden sections/fields, page breaks,
	/// signature and attachment placement, optional branding overrides, and the definition version the layout applies to.
	/// Dictionary members bind from Name[section.key] form fields.
	/// </summary>
	public class RecordDefinitionLayoutView : RecordsBaseView
	{
		public string DefinitionKey { get; set; }
		[Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
		public string DefinitionName { get; set; }
		[Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
		public RecordDefinitionSchema Schema { get; set; } = new RecordDefinitionSchema();
		[Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
		public string LayoutVersion { get; set; }
		[Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
		public string DepartmentLayoutVersion { get; set; }
		[Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
		public List<SelectListItem> Versions { get; set; } = new List<SelectListItem>();
		public int? AppliesToVersion { get; set; }
		public Dictionary<string, int> Order { get; set; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		public Dictionary<string, bool> Visible { get; set; } = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
		public Dictionary<string, string> Heading { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		public Dictionary<string, bool> PageBreak { get; set; } = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
		public Dictionary<string, bool> FieldVisible { get; set; } = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
		public string SignatureBlockPlacement { get; set; } = RecordsDefinitionLayoutConfig.SignatureAtEnd;
		public string AttachmentListStyle { get; set; } = RecordsDefinitionLayoutConfig.AttachmentsTable;
		public bool OverrideBranding { get; set; }
		public RecordsPrintLayoutConfig Branding { get; set; } = RecordsPrintLayoutConfig.Default();

		/// <summary>Schema sections in the posted or stored order; hidden sections stay listed so they can be shown again.</summary>
		public IEnumerable<string> OrderedSectionKeys()
		{
			var keys = (Schema?.Sections ?? new List<RecordSectionSchema>()).Select(s => s.Key).ToList();
			return keys.Select((k, i) => (Key: k, Sort: Order.TryGetValue(k, out var o) ? o : (i + 1) * 10 + 100000, Index: i)).OrderBy(t => t.Sort).ThenBy(t => t.Index).Select(t => t.Key);
		}

		public RecordsDefinitionLayoutConfig ToConfig()
		{
			var keys = OrderedSectionKeys().ToList();
			return new RecordsDefinitionLayoutConfig
			{
				AppliesToVersion = AppliesToVersion,
				SectionOrder = keys,
				HiddenSectionKeys = Visible.Where(v => !v.Value).Select(v => v.Key).ToList(),
				HiddenFieldKeys = FieldVisible.Where(v => !v.Value).Select(v => v.Key).ToList(),
				SectionHeadings = Heading.Where(h => !string.IsNullOrWhiteSpace(h.Value)).ToDictionary(h => h.Key, h => h.Value.Trim(), StringComparer.OrdinalIgnoreCase),
				PageBreakBeforeSectionKeys = PageBreak.Where(p => p.Value).Select(p => p.Key).ToList(),
				SignatureBlockPlacement = SignatureBlockPlacement,
				AttachmentListStyle = AttachmentListStyle,
				BrandingOverrides = OverrideBranding ? Branding : null
			};
		}

		public static RecordDefinitionLayoutView From(RecordDefinitionAggregate aggregate, RmsRecordDefinitionVersion version, RmsRecordPrintLayout stored, string departmentLayoutVersion)
		{
			var config = stored?.DefinitionConfig ?? RecordsDefinitionLayoutConfig.Default();
			var model = new RecordDefinitionLayoutView
			{
				DefinitionKey = aggregate.Definition.DefinitionKey,
				DefinitionName = aggregate.Definition.Name,
				Schema = version.Schema ?? new RecordDefinitionSchema(),
				LayoutVersion = stored?.LayoutVersion ?? RmsRecordPrintLayout.GeneratedLayoutVersion,
				DepartmentLayoutVersion = departmentLayoutVersion,
				Versions = aggregate.Versions.OrderByDescending(v => v.Version).Select(v => new SelectListItem { Value = v.Version.ToString(), Text = "v" + v.Version + " (" + ((RmsDefinitionVersionState)v.State) + ")" }).ToList(),
				AppliesToVersion = config.AppliesToVersion,
				SignatureBlockPlacement = config.SignatureBlockPlacement,
				AttachmentListStyle = config.AttachmentListStyle,
				OverrideBranding = config.BrandingOverrides != null,
				Branding = config.BrandingOverrides ?? RecordsPrintLayoutConfig.Default()
			};
			for (var i = 0; i < config.SectionOrder.Count; i++) model.Order[config.SectionOrder[i]] = (i + 1) * 10;
			foreach (var key in config.HiddenSectionKeys) model.Visible[key] = false;
			foreach (var key in config.HiddenFieldKeys) model.FieldVisible[key] = false;
			foreach (var pair in config.SectionHeadings) model.Heading[pair.Key] = pair.Value;
			foreach (var key in config.PageBreakBeforeSectionKeys) model.PageBreak[key] = true;
			return model;
		}
	}

	public class RecordDefinitionImpactView : RecordsBaseView
	{
		public RecordDefinitionAggregate Aggregate { get; set; }
		public RmsRecordDefinitionVersion VersionRow { get; set; }
		public RecordDefinitionImpactPreview Preview { get; set; }
		public RecordDefinitionDiff Diff { get; set; }
		public bool CanPublish { get; set; }
	}

	public class RecordDefinitionHistoryView : RecordsBaseView
	{
		public RecordDefinitionAggregate Aggregate { get; set; }
		public Department Department { get; set; }
		public List<RmsRecordDefinitionVersion> Versions { get; set; } = new List<RmsRecordDefinitionVersion>();
		public RecordDefinitionDiff Diff { get; set; }
		public int? From { get; set; }
		public int? To { get; set; }
		public RecordDefinitionMigrationResult Migration { get; set; }
		public bool CanManage { get; set; }
	}

	// ---- saved reports -----------------------------------------------------------------------------

	public class RecordSavedReportsIndexView : RecordsBaseView
	{
		public Department Department { get; set; }
		public List<RmsSavedReportDefinition> Reports { get; set; } = new List<RmsSavedReportDefinition>();
		public bool CanManage { get; set; }
	}

	public class RecordSavedReportEditView : RecordsBaseView
	{
		public string ReportId { get; set; }
		public long RowVersion { get; set; }
		public string Name { get; set; }
		public string Description { get; set; }
		public string DefinitionKey { get; set; }
		public int? DefinitionVersion { get; set; }
		public List<string> Columns { get; set; } = new List<string>();
		public string FiltersJson { get; set; }
		public string GroupByFieldKey { get; set; }
		public string AggregatesJson { get; set; }
		public string SortFieldKey { get; set; }
		public bool SortDescending { get; set; }
		public bool IncludeDrafts { get; set; }
		public int? WindowDays { get; set; }
		public string VersionMappingsJson { get; set; }
		public int MaxRowsPerRun { get; set; } = RmsSavedReportDefinition.MaxRows;
		public bool IncludeRestricted { get; set; }
		[Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
		public List<SelectListItem> Definitions { get; set; } = new List<SelectListItem>();
		[Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
		public RecordDefinitionSchema Schema { get; set; }
		[Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
		public List<RecordDefinitionIssue> Issues { get; set; } = new List<RecordDefinitionIssue>();
		[Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
		public bool CanIncludeRestricted { get; set; }
		public bool IsNew => string.IsNullOrWhiteSpace(ReportId);

		public RmsSavedReportDefinition ToReport()
		{
			var spec = new RecordReportSpec
			{
				Columns = (Columns ?? new List<string>()).Where(c => !string.IsNullOrWhiteSpace(c)).ToList(), GroupByFieldKey = string.IsNullOrWhiteSpace(GroupByFieldKey) ? null : GroupByFieldKey, SortFieldKey = string.IsNullOrWhiteSpace(SortFieldKey) ? null : SortFieldKey,
				SortDescending = SortDescending, IncludeDrafts = IncludeDrafts, WindowDays = WindowDays,
				Filters = string.IsNullOrWhiteSpace(FiltersJson) ? new List<RecordReportFilter>() : Newtonsoft.Json.JsonConvert.DeserializeObject<List<RecordReportFilter>>(FiltersJson) ?? new List<RecordReportFilter>(),
				Aggregates = string.IsNullOrWhiteSpace(AggregatesJson) ? new List<RecordReportAggregateSpec>() : Newtonsoft.Json.JsonConvert.DeserializeObject<List<RecordReportAggregateSpec>>(AggregatesJson) ?? new List<RecordReportAggregateSpec>(),
				VersionMappings = string.IsNullOrWhiteSpace(VersionMappingsJson) ? new Dictionary<int, Dictionary<string, string>>() : Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<int, Dictionary<string, string>>>(VersionMappingsJson) ?? new Dictionary<int, Dictionary<string, string>>()
			};
			return new RmsSavedReportDefinition { RmsSavedReportDefinitionId = ReportId, RowVersion = RowVersion, Name = Name, Description = Description, DefinitionKey = DefinitionKey, DefinitionVersion = DefinitionVersion, Spec = spec, MaxRowsPerRun = MaxRowsPerRun, IncludeRestricted = IncludeRestricted };
		}

		public static RecordSavedReportEditView From(RmsSavedReportDefinition report)
		{
			var spec = report.Spec;
			return new RecordSavedReportEditView
			{
				ReportId = report.RmsSavedReportDefinitionId, RowVersion = report.RowVersion, Name = report.Name, Description = report.Description, DefinitionKey = report.DefinitionKey, DefinitionVersion = report.DefinitionVersion,
				Columns = spec.Columns.ToList(), FiltersJson = Newtonsoft.Json.JsonConvert.SerializeObject(spec.Filters, Newtonsoft.Json.Formatting.Indented), GroupByFieldKey = spec.GroupByFieldKey,
				AggregatesJson = Newtonsoft.Json.JsonConvert.SerializeObject(spec.Aggregates, Newtonsoft.Json.Formatting.Indented), SortFieldKey = spec.SortFieldKey, SortDescending = spec.SortDescending, IncludeDrafts = spec.IncludeDrafts,
				WindowDays = spec.WindowDays, VersionMappingsJson = Newtonsoft.Json.JsonConvert.SerializeObject(spec.VersionMappings, Newtonsoft.Json.Formatting.Indented), MaxRowsPerRun = report.MaxRowsPerRun, IncludeRestricted = report.IncludeRestricted
			};
		}
	}

	public class RecordSavedReportRunView : RecordsBaseView
	{
		public RmsSavedReportDefinition Report { get; set; }
		public RecordReportResult Result { get; set; }
		public Department Department { get; set; }
	}

	// ---- deployments (RMS-1C, Preview) -------------------------------------------------------------

	public class RecordDeploymentsIndexView : RecordsBaseView
	{
		public Department Department { get; set; }
		public List<RmsExternalOrder> Orders { get; set; } = new List<RmsExternalOrder>();
		public bool IncludeClosed { get; set; }
		public bool CanCreate { get; set; }
		public bool IsDepartmentAdmin { get; set; }
	}

	public class RecordDeploymentNewView : RecordsBaseView
	{
		public string ProfileKey { get; set; } = RmsDeploymentProfiles.Generic;
		public string SourceScheme { get; set; }
		public string SourceSystem { get; set; }
		public string OrderNumber { get; set; }
		public string IncidentName { get; set; }
		public string IncidentNumber { get; set; }
		public string IncidentCountry { get; set; }
		public string IncidentSubdivision { get; set; }
		public string OrderingOffice { get; set; }
		public string DispatchOffice { get; set; }
		public string RequestingAgency { get; set; }
		public string ReceivingAgency { get; set; }
		public string SendingAgency { get; set; }
		public string DepartmentRole { get; set; } = "filling";
		public string CostCode { get; set; }
		public string AgreementReference { get; set; }
		public string CurrencyCode { get; set; }
		public string MeasurementSystem { get; set; }
		public string TimeZoneId { get; set; }
		public DateTime? SourceCapturedOn { get; set; }
		public string SourceVersion { get; set; }
		public string ArtifactSafeUrl { get; set; }
		public int? StationGroupId { get; set; }
		public List<RecordDeploymentFillInput> Fills { get; set; } = new List<RecordDeploymentFillInput>();
		[Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
		public List<SelectListItem> Profiles { get; set; } = new List<SelectListItem>();
		[Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
		public List<SelectListItem> Stations { get; set; } = new List<SelectListItem>();
		[Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
		public List<SelectListItem> Personnel { get; set; } = new List<SelectListItem>();
		[Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
		public List<SelectListItem> AvailableUnits { get; set; } = new List<SelectListItem>();
		[Microsoft.AspNetCore.Mvc.ModelBinding.BindNever]
		public Department Department { get; set; }
	}

	public class RecordDeploymentDetailsView : RecordsBaseView
	{
		public RecordDeploymentAggregate Deployment { get; set; }
		public Department Department { get; set; }
		public Dictionary<string, string> PersonnelNames { get; set; } = new Dictionary<string, string>();
		public bool CanEdit { get; set; }
		public string ProvenanceStatement { get; set; }
		public RecordDeploymentFillInput NewFill { get; set; } = new RecordDeploymentFillInput();
		public List<SelectListItem> Personnel { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> AvailableUnits { get; set; } = new List<SelectListItem>();
	}

	// ---- definition-driven record form (RMS-1B) ----------------------------------------------------

	/// <summary>
	/// A Record on a department definition: the pinned schema, the stored or posted values, the rule evaluation and the
	/// reference lists the renderer needs. Posts back as RecordEditView with Values[i].* fields.
	/// </summary>
	public class RecordDefinitionFormView : RecordsBaseView
	{
		public string RecordId { get; set; }
		public long RowVersion { get; set; }
		public string DefinitionKey { get; set; }
		public string DefinitionName { get; set; }
		public int DefinitionVersion { get; set; }
		public string DraftReference { get; set; }
		public string RecordNumber { get; set; }
		public bool IsAmendment { get; set; }
		public bool IsNew => string.IsNullOrWhiteSpace(RecordId);
		public int? CallId { get; set; }
		public int? StationGroupId { get; set; }
		public string ExternalId { get; set; }
		public DateTime? StartedOn { get; set; }
		public DateTime? EndedOn { get; set; }
		public RecordDefinitionSchema Schema { get; set; } = new RecordDefinitionSchema();
		public RecordValueSet Values { get; set; }
		public RecordRuleEvaluation Evaluation { get; set; } = new RecordRuleEvaluation();
		/// <summary>Raw inputs from a failed post; they win over the stored values when re-rendering.</summary>
		public List<RecordValueInput> PostedValues { get; set; }
		public RmsLifecyclePreset LifecyclePreset { get; set; }
		public string MinimumClientCapability { get; set; }
		public string ProvenanceStatement { get; set; }
		public bool IsPreview { get; set; }
		public bool CanViewRestricted { get; set; }
		public bool CanFinalize { get; set; }
		public bool FinalizeAfterSave { get; set; }
		public bool Attested { get; set; }
		public string ReasonCode { get; set; }
		public string ReasonText { get; set; }
		public int AttachmentClassification { get; set; } = 1;
		public Department Department { get; set; }
		public List<SelectListItem> Stations { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> Personnel { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> AvailableUnits { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> Calls { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> Contacts { get; set; } = new List<SelectListItem>();
		public List<SelectListItem> Attachments { get; set; } = new List<SelectListItem>();
		public List<RmsOperationalRecord> DuplicateCandidates { get; set; } = new List<RmsOperationalRecord>();

		private List<RecordValueInput> _inputs;
		/// <summary>Posted inputs when present, else the stored values as inputs.</summary>
		public List<RecordValueInput> Inputs => _inputs ??= (PostedValues != null && PostedValues.Count > 0 ? PostedValues : Values?.ToInputs()) ?? new List<RecordValueInput>();

		public RecordValueInput Input(string sectionKey, string rowKey, string fieldKey)
			=> Inputs.FirstOrDefault(i => string.Equals(i.FieldKey, fieldKey, StringComparison.OrdinalIgnoreCase) && (rowKey == null ? string.IsNullOrEmpty(i.RowKey) || !IsRepeating(sectionKey) : string.Equals(i.RowKey, rowKey, StringComparison.OrdinalIgnoreCase)));

		private bool IsRepeating(string sectionKey) => Schema.FindSection(sectionKey)?.Repeating == true;

		/// <summary>Row keys of a repeating section in ordinal order (at least one blank row is rendered by the view).</summary>
		public List<string> RowKeys(string sectionKey)
			=> Inputs.Where(i => string.Equals(i.SectionKey, sectionKey, StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(i.SectionKey) && Schema.SectionOf(i.FieldKey)?.Key.Equals(sectionKey, StringComparison.OrdinalIgnoreCase) == true)
				.Where(i => !string.IsNullOrEmpty(i.RowKey)).GroupBy(i => i.RowKey, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Min(i => i.Ordinal)).Select(g => g.Key).ToList();

		/// <summary>Whether a stored restricted cell is withheld for this viewer (the input renders disabled and blank).</summary>
		public bool IsWithheld(string fieldKey) => !CanViewRestricted && Schema.FindField(fieldKey)?.Classification == RmsFieldClassification.Restricted;
	}

	/// <summary>External ordering-system connectors (RMS plan section 4.1): the department's connectors and the open reconciliation across them.</summary>
	public class RecordDeploymentConnectorsIndexView : RecordsBaseView
	{
		public Department Department { get; set; }
		public bool ConnectorsEnabled { get; set; }
		public List<RmsExternalOrderConnector> Connectors { get; set; } = new List<RmsExternalOrderConnector>();
		public List<RecordDeploymentReconciliationItem> Reconciliation { get; set; } = new List<RecordDeploymentReconciliationItem>();
		public string InboundToken { get; set; }
	}

	/// <summary>One connector: the editable input, and when it exists, its state, run log, reconciliation and the one-time inbound token.</summary>
	public class RecordDeploymentConnectorEditView : RecordsBaseView
	{
		public bool IsNew { get; set; }
		public string Id { get; set; }
		public long RowVersion { get; set; }
		public string ProviderKey { get; set; } = RmsExternalOrderConnectorProviders.Generic;
		public string Name { get; set; }
		public string SourceSystem { get; set; }
		public string SourceScheme { get; set; }
		public string ProfileKey { get; set; }
		public string BaseUrl { get; set; }
		public string CredentialKind { get; set; } = RmsConnectorCredentialKinds.None;
		public string CredentialHeaderName { get; set; }
		public string Credential { get; set; }
		public bool ReadEnabled { get; set; } = true;
		public int PollIntervalMinutes { get; set; } = 60;
		public int MaxRequestsPerHour { get; set; } = 12;
		public string TermsReference { get; set; }

		[Microsoft.AspNetCore.Mvc.ModelBinding.BindNever] public RmsExternalOrderConnector Connector { get; set; }
		[Microsoft.AspNetCore.Mvc.ModelBinding.BindNever] public Department Department { get; set; }
		[Microsoft.AspNetCore.Mvc.ModelBinding.BindNever] public bool ConnectorsEnabled { get; set; }
		[Microsoft.AspNetCore.Mvc.ModelBinding.BindNever] public int MinPollIntervalMinutes { get; set; }
		[Microsoft.AspNetCore.Mvc.ModelBinding.BindNever] public string InboundToken { get; set; }
		[Microsoft.AspNetCore.Mvc.ModelBinding.BindNever] public List<SelectListItem> Providers { get; set; } = new List<SelectListItem>();
		[Microsoft.AspNetCore.Mvc.ModelBinding.BindNever] public List<SelectListItem> Profiles { get; set; } = new List<SelectListItem>();
		[Microsoft.AspNetCore.Mvc.ModelBinding.BindNever] public List<SelectListItem> CredentialKinds { get; set; } = new List<SelectListItem>();
		[Microsoft.AspNetCore.Mvc.ModelBinding.BindNever] public List<RmsExternalOrderConnectorRun> Runs { get; set; } = new List<RmsExternalOrderConnectorRun>();
		[Microsoft.AspNetCore.Mvc.ModelBinding.BindNever] public List<RecordDeploymentReconciliationItem> Reconciliation { get; set; } = new List<RecordDeploymentReconciliationItem>();
		[Microsoft.AspNetCore.Mvc.ModelBinding.BindNever] public Dictionary<string, string> PersonnelNames { get; set; } = new Dictionary<string, string>();

		public RecordDeploymentConnectorInput ToInput() => new RecordDeploymentConnectorInput
		{
			ProviderKey = ProviderKey, Name = Name, SourceSystem = SourceSystem, SourceScheme = SourceScheme, ProfileKey = ProfileKey, BaseUrl = BaseUrl, CredentialKind = CredentialKind,
			CredentialHeaderName = CredentialHeaderName, Credential = Credential, ReadEnabled = ReadEnabled, WriteEnabled = false, PollIntervalMinutes = PollIntervalMinutes,
			MaxRequestsPerHour = MaxRequestsPerHour, TermsReference = TermsReference
		};

		public static RecordDeploymentConnectorEditView From(RmsExternalOrderConnector c) => new RecordDeploymentConnectorEditView
		{
			Id = c.RmsExternalOrderConnectorId, RowVersion = c.RowVersion, ProviderKey = c.ProviderKey, Name = c.Name, SourceSystem = c.SourceSystem, SourceScheme = c.SourceScheme, ProfileKey = c.ProfileKey,
			BaseUrl = c.BaseUrl, CredentialKind = c.CredentialKind, CredentialHeaderName = c.CredentialHeaderName, ReadEnabled = c.ReadEnabled, PollIntervalMinutes = c.PollIntervalMinutes,
			MaxRequestsPerHour = c.MaxRequestsPerHour, TermsReference = c.TermsReference, Connector = c
		};
	}
}
