using System;
using System.Collections.Generic;
using System.Linq;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Services.Records;
using Resgrid.Web.Services.Models.v4.Records;

namespace Resgrid.Web.Services.Helpers
{
	/// <summary>Entity to DTO mapping for the RMS-1B/1C v4 surfaces. Restricted typed values are withheld here, never in the client.</summary>
	public static class RecordsRms1bApiMapper
	{
		public static List<RecordValueInput> ToValueInputs(IEnumerable<RecordValueInputData> inputs)
			=> (inputs ?? Enumerable.Empty<RecordValueInputData>()).Where(i => i != null).Select(i => new RecordValueInput
			{
				SectionKey = i.SectionKey, FieldKey = i.FieldKey, RowKey = i.RowKey, Ordinal = i.Ordinal, Value = i.Value, Values = i.Values, ReferenceType = i.ReferenceType, ReferenceId = i.ReferenceId,
				UnitCode = i.UnitCode, CurrencyCode = i.CurrencyCode, OffsetMinutes = i.OffsetMinutes
			}).ToList();

		public static RecordValuesData ToValues(RecordValueSet set, RecordDefinitionSchema schema, bool canViewRestricted, IRecordTypedValuesService typedValues = null)
		{
			if (set == null) return null;
			var data = new RecordValuesData { DefinitionKey = set.DefinitionKey, DefinitionVersion = set.DefinitionVersion, WithheldFieldKeys = set.WithheldFieldKeys.ToList() };
			foreach (var section in set.Sections)
			{
				var sectionData = new RecordValueSectionData { SectionKey = section.SectionKey, Label = section.Label, Repeating = section.Repeating };
				foreach (var row in section.Rows)
				{
					var rowData = new RecordValueRowData { RowKey = row.RowKey, Ordinal = row.Ordinal };
					foreach (var cell in row.Cells)
					{
						var withheld = cell.Withheld || cell.Classification == RmsFieldClassification.Restricted && !canViewRestricted;
						if (withheld && !data.WithheldFieldKeys.Contains(cell.FieldKey)) data.WithheldFieldKeys.Add(cell.FieldKey);
						rowData.Cells.Add(new RecordValueCellData
						{
							FieldKey = cell.FieldKey, Label = cell.Label, Type = cell.Type.ToString(), Classification = cell.Classification.ToString(), Withheld = withheld,
							Display = withheld ? (cell.Display == null ? null : RecordTypedValuesService.Redacted) : cell.Display,
							Value = withheld ? null : cell.Value, Values = withheld ? null : cell.Values, ReferenceType = withheld ? null : cell.ReferenceType, ReferenceId = withheld ? null : cell.ReferenceId,
							UnitCode = cell.UnitCode, CurrencyCode = cell.CurrencyCode, OffsetMinutes = cell.OffsetMinutes, Number = withheld ? null : cell.Number, CanonicalNumber = withheld ? null : cell.CanonicalNumber, CanonicalUnitCode = cell.CanonicalUnitCode
						});
					}
					sectionData.Rows.Add(rowData);
				}
				data.Sections.Add(sectionData);
			}
			if (schema != null && typedValues != null)
			{
				var evaluation = typedValues.EvaluateRules(schema, set);
				data.HiddenSectionKeys = evaluation.HiddenSectionKeys.ToList();
				data.HiddenFieldKeys = evaluation.HiddenFieldKeys.ToList();
				data.RequiredFieldKeys = evaluation.RequiredFieldKeys.ToList();
				data.RowRules = evaluation.Rows.Values.Where(r => r.HiddenFieldKeys.Count > 0 || r.RequiredFieldKeys.Count > 0)
					.Select(r => new RecordRowRulesData { SectionKey = r.SectionKey, RowKey = r.RowKey, HiddenFieldKeys = r.HiddenFieldKeys.ToList(), RequiredFieldKeys = r.RequiredFieldKeys.ToList() }).ToList();
			}
			return data;
		}

		public static RecordDefinitionVersionData ToVersion(RmsRecordDefinitionVersion v)
		{
			if (v == null) return null;
			return new RecordDefinitionVersionData
			{
				VersionId = v.RmsRecordDefinitionVersionId, DefinitionKey = v.DefinitionKey, Version = v.Version, State = ((RmsDefinitionVersionState)v.State).ToString(),
				LifecyclePreset = ((RmsLifecyclePreset)v.LifecyclePreset).ToString(), ReviewerRoleIds = RecordDefinitionsService.ParseIds(v.ReviewerRoleIds), ApproverRoleIds = RecordDefinitionsService.ParseIds(v.ApproverRoleIds),
				ReviewDueHours = v.ReviewDueHours, ApproveDueHours = v.ApproveDueHours, RequireAuthorAttestation = v.RequireAuthorAttestation, Numbering = v.Numbering, RetentionYears = v.RetentionYears,
				Classification = ((RmsFieldClassification)v.Classification).ToString(), Schema = v.Schema, SchemaChecksum = v.SchemaChecksum, MinimumClientCapability = v.MinimumClientCapability, ClientSurface = v.ClientSurface,
				MigrationMap = RecordDefinitionsService.ToDraftInput(v).MigrationMap, ChangeNotes = v.ChangeNotes, PublishedOn = v.PublishedOn, PublishedByUserId = v.PublishedByUserId, RetiredOn = v.RetiredOn,
				CreatedOn = v.CreatedOn, ModifiedOn = v.ModifiedOn, RowVersion = v.RowVersion, ETag = RecordsApiContract.ToETag(v.RowVersion)
			};
		}

		public static RecordDefinitionDetailData ToDefinition(RecordDefinitionAggregate aggregate)
		{
			var d = aggregate.Definition;
			return new RecordDefinitionDetailData
			{
				DefinitionId = d.RmsRecordDefinitionId, Key = d.DefinitionKey, Name = d.Name, Category = d.Category, Description = d.Description, Owner = ((RmsDefinitionOwner)d.Owner).ToString(),
				TemplateKey = d.TemplateKey, JurisdictionProfileKey = d.JurisdictionProfileKey, PermittedSubjectTypes = d.PermittedSubjectTypes, CurrentPublishedVersion = d.CurrentPublishedVersion, LatestVersion = d.LatestVersion,
				IsRetired = d.IsRetired, RetiredOn = d.RetiredOn, RetiredReason = d.RetiredReason, RowVersion = d.RowVersion, ETag = RecordsApiContract.ToETag(d.RowVersion),
				Versions = aggregate.Versions.OrderBy(v => v.Version).Select(ToVersion).ToList()
			};
		}

		/// <summary>Department definitions as the capability manifest lists them (plan 5.4), beside the locked ones.</summary>
		public static RecordDefinitionData ToDefinitionData(RmsRecordDefinitionVersion version, RmsRecordDefinition definition)
		{
			var schema = version.Schema;
			return new RecordDefinitionData
			{
				Key = version.DefinitionKey, Version = version.Version, Name = definition?.Name ?? version.DefinitionKey, RecordType = null, RecordKind = RmsRecordKind.Operational.ToString(),
				LifecyclePreset = version.LifecyclePreset, LifecyclePresetName = ((RmsLifecyclePreset)version.LifecyclePreset).ToString(), Cardinality = RmsRecordCardinality.MultiplePerCall.ToString(),
				Restricted = version.Classification != (int)RmsFieldClassification.Standard || schema.AllFields().Any(f => f.Classification != RmsFieldClassification.Standard),
				NumberPrefix = version.Numbering.Prefix, RequiresCall = false, SupportsParticipants = true, SupportsUnits = true, SupportsAttachments = version.ClientSurface.AllowAttachments,
				MinimumClientCapability = version.MinimumClientCapability ?? RecordsClientCapabilities.Derive(schema), Locked = false,
				Fields = schema.AllFields().Select(f => new RecordFieldData { Key = f.Key, Section = schema.SectionOf(f.Key)?.Key, Type = f.Type.ToString(), Required = f.Required, RequiredToFinalize = f.RequiredToFinalize, Restricted = f.Classification != RmsFieldClassification.Standard }).ToList()
			};
		}

		public static RecordTemplateRenderingData ToRendering(RecordTemplateRendering rendering)
		{
			var t = rendering.Template;
			return new RecordTemplateRenderingData
			{
				TemplateKey = t.Key, Name = t.Name, Category = t.Category, Description = t.Description, PackKey = t.PackKey, LifecyclePreset = t.LifecyclePreset.ToString(), NumberPrefix = t.NumberPrefix, PermittedSubjectTypes = t.PermittedSubjectTypes,
				ProfileKey = rendering.ProfileKey, Locale = rendering.Locale, MeasurementSystem = rendering.MeasurementSystem, CurrencyCode = rendering.CurrencyCode, ArtifactStatus = rendering.ArtifactStatus.ToString(),
				ProvenanceStatement = rendering.ProvenanceStatement, Sources = rendering.Sources, MinimumClientCapability = RecordsClientCapabilities.Derive(rendering.Schema), Schema = rendering.Schema
			};
		}

		public static RecordSavedReportData ToReport(RmsSavedReportDefinition r) => new RecordSavedReportData
		{
			ReportId = r.RmsSavedReportDefinitionId, Name = r.Name, Description = r.Description, DefinitionKey = r.DefinitionKey, DefinitionVersion = r.DefinitionVersion, Spec = r.Spec, MaxRowsPerRun = r.MaxRowsPerRun,
			IncludeRestricted = r.IncludeRestricted, LastRunOn = r.LastRunOn, LastRunByUserId = r.LastRunByUserId, CreatedOn = r.CreatedOn, ModifiedOn = r.ModifiedOn, RowVersion = r.RowVersion, ETag = RecordsApiContract.ToETag(r.RowVersion)
		};

		public static RmsSavedReportDefinition ToReport(SaveRecordSavedReportInput input) => new RmsSavedReportDefinition
		{
			RmsSavedReportDefinitionId = input.ReportId, RowVersion = input.RowVersion, Name = input.Name?.Trim(), Description = input.Description, DefinitionKey = RecordDefinitionKeys.NormalizeKey(input.DefinitionKey),
			DefinitionVersion = input.DefinitionVersion, Spec = input.Spec ?? new RecordReportSpec(), MaxRowsPerRun = input.MaxRowsPerRun, IncludeRestricted = input.IncludeRestricted
		};

		public static RecordExportTemplateData ToTemplate(RmsExportTemplate t) => new RecordExportTemplateData
		{
			TemplateId = t.RmsExportTemplateId, TemplateKey = t.TemplateKey, Name = t.Name, Description = t.Description, Format = ((RmsExportFormat)t.Format).ToString(), Scope = ((RmsExportScope)t.Scope).ToString(),
			DefinitionKeys = (t.DefinitionKeysCsv ?? string.Empty).Split(',').Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList(),
			Columns = string.IsNullOrWhiteSpace(t.ColumnsJson) ? new List<string>() : Newtonsoft.Json.JsonConvert.DeserializeObject<List<string>>(t.ColumnsJson) ?? new List<string>(),
			IncludeNarrative = t.IncludeNarrative, IncludeRestricted = t.IncludeRestricted, EgressAcknowledgedOn = t.EgressAcknowledgedOn, EgressAcknowledgedByUserId = t.EgressAcknowledgedByUserId, FileNameTemplate = t.FileNameTemplate,
			IncludeHeader = t.IncludeHeader, Delimiter = t.Delimiter == "\t" ? "tab" : t.Delimiter, ScheduleKind = ((RmsExportScheduleKind)t.ScheduleKind).ToString(), ScheduleHourLocal = t.ScheduleHourLocal, ScheduleDayOfWeek = t.ScheduleDayOfWeek,
			ScheduleDayOfMonth = t.ScheduleDayOfMonth, WindowDays = t.WindowDays, NextRunOn = t.NextRunOn, LastRunOn = t.LastRunOn, IsEnabled = t.IsEnabled, CreatedOn = t.CreatedOn, ModifiedOn = t.ModifiedOn, RowVersion = t.RowVersion, ETag = RecordsApiContract.ToETag(t.RowVersion)
		};

		public static RmsExportTemplate ToTemplate(SaveRecordExportTemplateInput input) => new RmsExportTemplate
		{
			RmsExportTemplateId = input.TemplateId, RowVersion = input.RowVersion, TemplateKey = input.TemplateKey, Name = input.Name, Description = input.Description, Format = input.Format, Scope = input.Scope,
			DefinitionKeysCsv = string.Join(",", input.DefinitionKeys ?? new List<string>()), ColumnsJson = Newtonsoft.Json.JsonConvert.SerializeObject(input.Columns ?? new List<string>()),
			IncludeNarrative = input.IncludeNarrative, IncludeRestricted = input.IncludeRestricted, FileNameTemplate = input.FileNameTemplate, IncludeHeader = input.IncludeHeader,
			Delimiter = input.Delimiter == "tab" ? "\t" : input.Delimiter, ScheduleKind = input.ScheduleKind, ScheduleHourLocal = input.ScheduleHourLocal, ScheduleDayOfWeek = input.ScheduleDayOfWeek, ScheduleDayOfMonth = input.ScheduleDayOfMonth,
			WindowDays = input.WindowDays, IsEnabled = input.IsEnabled
		};

		public static RecordExportRunData ToRun(RmsExportRun r) => new RecordExportRunData
		{
			RunId = r.RmsExportRunId, TemplateId = r.TemplateId, TemplateKey = r.TemplateKey, Trigger = ((RmsExportTrigger)r.Trigger).ToString(), RecordId = r.RecordId, WindowStart = r.WindowStart, WindowEnd = r.WindowEnd,
			RecordCount = r.RecordCount, FileName = r.FileName, ContentType = r.ContentType, ByteSize = r.ByteSize, Checksum = r.Checksum, Redacted = r.Redacted, GeneratedOn = r.GeneratedOn, GeneratedByUserId = r.GeneratedByUserId,
			WorkflowRunId = r.WorkflowRunId, ExpiresOn = r.ExpiresOn
		};

		public static RecordDeploymentFillData ToFill(RmsExternalOrderFill f) => new RecordDeploymentFillData
		{
			FillId = f.RmsExternalOrderFillId, RequestNumber = f.RequestNumber, ParentRequestNumber = f.ParentRequestNumber, RequestCategory = f.RequestCategory, FillNumber = f.FillNumber, ResourceKind = f.ResourceKind, ResourceType = f.ResourceType,
			ResourceTypeScheme = f.ResourceTypeScheme, Position = f.Position, PositionScheme = f.PositionScheme, IsTrainee = f.IsTrainee, HomeUnit = f.HomeUnit, HostAgency = f.HostAgency, AgencyUnitId = f.AgencyUnitId, PointOfHire = f.PointOfHire,
			CostCode = f.CostCode, AgreementReference = f.AgreementReference, AssignedUserId = f.AssignedUserId, AssignedUnitId = f.AssignedUnitId, Status = ((RmsDeploymentFillStatus)f.Status).ToString(), DeclineReason = f.DeclineReason,
			RequestedOn = f.RequestedOn, NeededOn = f.NeededOn, FilledOn = f.FilledOn, MobilizedOn = f.MobilizedOn, CheckedInOn = f.CheckedInOn, AssignedOn = f.AssignedOn, ReleasedOn = f.ReleasedOn, DemobilizedOn = f.DemobilizedOn, ReturnedOn = f.ReturnedOn,
			CapturedOffsetMinutes = f.CapturedOffsetMinutes, Notes = f.Notes, RowVersion = f.RowVersion
		};

		public static RecordDeploymentData ToDeployment(RecordDeploymentAggregate aggregate)
		{
			var o = aggregate.Order;
			var pack = RecordTemplateCatalog.PackOf(RecordDeploymentsService.DeploymentTemplateKey);
			return new RecordDeploymentData
			{
				OrderId = o.RmsExternalOrderId, RecordId = o.RecordId, RecordNumber = aggregate.Record?.Record?.RecordNumber ?? aggregate.Record?.Record?.DraftReference, RecordState = aggregate.Record == null ? null : ((RmsRecordState)aggregate.Record.Record.State).ToString(),
				ProfileKey = o.ProfileKey, ProfileVersion = o.ProfileVersion, HomeProfileKey = o.HomeProfileKey, HostProfileKey = o.HostProfileKey, SourceScheme = o.SourceScheme, SourceSystem = o.SourceSystem, OrderNumber = o.OrderNumber,
				IncidentName = o.IncidentName, IncidentNumber = o.IncidentNumber, IncidentCountry = o.IncidentCountry, IncidentSubdivision = o.IncidentSubdivision, OrderingOffice = o.OrderingOffice, DispatchOffice = o.DispatchOffice,
				RequestingAgency = o.RequestingAgency, ReceivingAgency = o.ReceivingAgency, SendingAgency = o.SendingAgency, DepartmentRole = o.DepartmentRole, CostCode = o.CostCode, AgreementReference = o.AgreementReference,
				CurrencyCode = o.CurrencyCode, MeasurementSystem = o.MeasurementSystem, TimeZoneId = o.TimeZoneId, CapturedOffsetMinutes = o.CapturedOffsetMinutes, SourceCapturedOn = o.SourceCapturedOn, SourceVersion = o.SourceVersion,
				ArtifactFileName = o.ArtifactFileName, ArtifactContentType = o.ArtifactContentType, ArtifactChecksum = o.ArtifactChecksum, HasArtifact = o.ArtifactChecksum != null, ArtifactSafeUrl = o.ArtifactSafeUrl,
				Status = ((RmsExternalOrderStatus)o.Status).ToString(), MobilizedOn = o.MobilizedOn, ReleasedOn = o.ReleasedOn, ClosedOutOn = o.ClosedOutOn, CloseoutNotes = o.CloseoutNotes, AllReturned = aggregate.AllReturned,
				IsPreview = pack?.IsPreview ?? true, ProvenanceStatement = "Preview: created from an external order snapshot; no claim that NWCG, CIFFC or a member agency accepts this output until a real order has been filled and reconciled.",
				CreatedOn = o.CreatedOn, ModifiedOn = o.ModifiedOn, RowVersion = o.RowVersion, ETag = RecordsApiContract.ToETag(o.RowVersion), Fills = aggregate.Fills.Select(ToFill).ToList()
			};
		}

		public static RecordDeploymentCreateInput ToCreateInput(CreateRecordDeploymentInput input, RmsOriginClient origin)
		{
			byte[] artifact = null;
			if (!string.IsNullOrWhiteSpace(input.ArtifactBase64))
			{
				try { artifact = Convert.FromBase64String(input.ArtifactBase64); }
				catch (FormatException) { throw new ArgumentException("ArtifactBase64 is not valid base64.", nameof(input)); }
			}
			return new RecordDeploymentCreateInput
			{
				ProfileKey = input.ProfileKey, HomeProfileKey = input.HomeProfileKey, HostProfileKey = input.HostProfileKey, SourceScheme = input.SourceScheme, SourceSystem = input.SourceSystem, OrderNumber = input.OrderNumber,
				IncidentName = input.IncidentName, IncidentNumber = input.IncidentNumber, IncidentCountry = input.IncidentCountry, IncidentSubdivision = input.IncidentSubdivision, OrderingOffice = input.OrderingOffice, DispatchOffice = input.DispatchOffice,
				RequestingAgency = input.RequestingAgency, ReceivingAgency = input.ReceivingAgency, SendingAgency = input.SendingAgency, DepartmentRole = input.DepartmentRole, CostCode = input.CostCode, AgreementReference = input.AgreementReference,
				CurrencyCode = input.CurrencyCode, MeasurementSystem = input.MeasurementSystem, TimeZoneId = input.TimeZoneId, CapturedOffsetMinutes = input.CapturedOffsetMinutes, SourceCapturedOn = input.SourceCapturedOn, SourceVersion = input.SourceVersion,
				ArtifactFileName = input.ArtifactFileName, ArtifactContentType = input.ArtifactContentType, ArtifactData = artifact, ArtifactSafeUrl = input.ArtifactSafeUrl, StationGroupId = input.StationGroupId, IdempotencyKey = input.IdempotencyKey,
				OriginClient = origin, Fills = input.Fills ?? new List<RecordDeploymentFillInput>()
			};
		}
	}
}
