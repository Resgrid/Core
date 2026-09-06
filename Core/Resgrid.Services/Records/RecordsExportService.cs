using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Helpers;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;

namespace Resgrid.Services.Records
{
	/// <summary>
	/// Department-authored report exports (RMS plan sections 4.7, 4.10, 5.6 and 5.9.2). A template is a finite
	/// column list from <see cref="RecordsExportFieldCatalog"/>, a format, a scope and an optional schedule; a
	/// render walks the authorized records, resolves every cataloged column for the caller (attended grant, or
	/// the acknowledged export egress lane for the worker), and stores the bytes as an RmsExportRun. Workflow
	/// steps carry the run as an attachment; agencies without an API get the file by email, FTP/SFTP or a
	/// cloud drop through the executors the department already configured.
	/// </summary>
	public class RecordsExportService : IRecordsExportService
	{
		public const int RunRetentionDays = 30;
		public const int MaxWindowRecords = 5000;
		private static readonly Regex KeyPattern = new Regex("^[a-z0-9][a-z0-9-]{1,62}$", RegexOptions.Compiled);
		private static readonly Regex FileNamePattern = new Regex(@"^[A-Za-z0-9 _.\-{}]{1,120}$", RegexOptions.Compiled);
		private static readonly int[] FinalizedStates = { (int)RmsRecordState.Finalized, (int)RmsRecordState.Amended, (int)RmsRecordState.Submitted, (int)RmsRecordState.Accepted, (int)RmsRecordState.Rejected, (int)RmsRecordState.Corrected };

		private readonly IRmsExportTemplatesRepository _templates;
		private readonly IRmsExportRunsRepository _runs;
		private readonly IRecordsService _records;
		private readonly IIncidentReportsService _incidents;
		private readonly IRmsOperationalRecordsRepository _recordsRepository;
		private readonly IRmsIncidentReportsRepository _incidentsRepository;
		private readonly IRecordsAuthorizationService _authorization;
		private readonly IRecordsProtectionService _protection;
		private readonly IDomainEventOutboxService _outbox;
		private readonly IRmsAccessAuditsRepository _audits;
		private readonly IDepartmentsService _departments;
		private readonly IDepartmentGroupsService _groups;
		private readonly IUserProfileService _profiles;
		private readonly IPdfProvider _pdf;
		private readonly IUnitOfWork _unitOfWork;

		public RecordsExportService(IRmsExportTemplatesRepository templates, IRmsExportRunsRepository runs, IRecordsService records, IIncidentReportsService incidents,
			IRmsOperationalRecordsRepository recordsRepository, IRmsIncidentReportsRepository incidentsRepository, IRecordsAuthorizationService authorization,
			IRecordsProtectionService protection, IDomainEventOutboxService outbox, IRmsAccessAuditsRepository audits, IDepartmentsService departments,
			IDepartmentGroupsService groups, IUserProfileService profiles, IPdfProvider pdf, IUnitOfWork unitOfWork)
		{
			_templates = templates;
			_runs = runs;
			_records = records;
			_incidents = incidents;
			_recordsRepository = recordsRepository;
			_incidentsRepository = incidentsRepository;
			_authorization = authorization;
			_protection = protection;
			_outbox = outbox;
			_audits = audits;
			_departments = departments;
			_groups = groups;
			_profiles = profiles;
			_pdf = pdf;
			_unitOfWork = unitOfWork;
		}

		#region Templates

		public async Task<List<RmsExportTemplate>> GetTemplatesAsync(int departmentId)
			=> (await _templates.GetForDepartmentAsync(departmentId))?.ToList() ?? new List<RmsExportTemplate>();

		public Task<RmsExportTemplate> GetTemplateAsync(int departmentId, string templateId)
			=> string.IsNullOrWhiteSpace(templateId) ? Task.FromResult<RmsExportTemplate>(null) : _templates.GetByIdForDepartmentAsync(departmentId, templateId);

		public Task<RmsExportTemplate> GetTemplateByKeyAsync(int departmentId, string templateKey)
			=> string.IsNullOrWhiteSpace(templateKey) ? Task.FromResult<RmsExportTemplate>(null) : _templates.GetByKeyAsync(departmentId, templateKey.Trim().ToLowerInvariant());

		public async Task<RecordsExportTemplateValidation> ValidateAsync(int departmentId, string userId, RmsExportTemplate template)
		{
			var result = new RecordsExportTemplateValidation();
			if (template == null)
			{
				result.Errors.Add("A template is required.");
				return result;
			}

			if (string.IsNullOrWhiteSpace(template.Name) || template.Name.Trim().Length > 200)
				result.Errors.Add("Give the export a name of up to 200 characters.");
			var key = (template.TemplateKey ?? string.Empty).Trim().ToLowerInvariant();
			if (!KeyPattern.IsMatch(key))
				result.Errors.Add("The key must be 2-63 lower-case letters, digits or hyphens; it is the stable name a Workflow step and an agency import rely on.");
			else
			{
				var existing = await _templates.GetByKeyAsync(departmentId, key);
				if (existing != null && existing.RmsExportTemplateId != template.RmsExportTemplateId)
					result.Errors.Add("Another export already uses that key.");
			}
			if (!Enum.IsDefined(typeof(RmsExportFormat), template.Format))
				result.Errors.Add("Choose CSV, JSON or PDF.");
			if (!Enum.IsDefined(typeof(RmsExportScope), template.Scope))
				result.Errors.Add("Choose whether the export covers the triggering record or a window of records.");
			if (!Enum.IsDefined(typeof(RmsExportScheduleKind), template.ScheduleKind))
				result.Errors.Add("Choose a schedule.");
			if (template.ScheduleHourLocal < 0 || template.ScheduleHourLocal > 23)
				result.Errors.Add("The schedule hour must be 0-23.");
			var kind = Enum.IsDefined(typeof(RmsExportScheduleKind), template.ScheduleKind) ? (RmsExportScheduleKind)template.ScheduleKind : RmsExportScheduleKind.None;
			if (kind == RmsExportScheduleKind.Weekly && (template.ScheduleDayOfWeek < 0 || template.ScheduleDayOfWeek > 6))
				result.Errors.Add("The schedule weekday must be 0 (Sunday) to 6 (Saturday).");
			if (kind == RmsExportScheduleKind.Monthly && (template.ScheduleDayOfMonth < 1 || template.ScheduleDayOfMonth > 28))
				result.Errors.Add("The schedule day of month must be 1-28 so it exists in every month.");
			if (template.WindowDays < 0 || template.WindowDays > 366)
				result.Errors.Add("The window must be 0-366 days (0 uses the schedule period).");
			if ((RmsExportScheduleKind)template.ScheduleKind != RmsExportScheduleKind.None && (RmsExportScope)template.Scope != RmsExportScope.Window)
				result.Errors.Add("A scheduled export covers a window of records; a triggering-record export runs from a Workflow instead.");
			if (!string.IsNullOrWhiteSpace(template.FileNameTemplate) && !FileNamePattern.IsMatch(template.FileNameTemplate.Trim()))
				result.Errors.Add("The file name may use letters, digits, spaces, dot, dash, underscore and the {template}, {date} and {record} tokens.");
			if (!string.IsNullOrEmpty(template.Delimiter) && template.Delimiter != "," && template.Delimiter != ";" && template.Delimiter != "|" && template.Delimiter != "\t")
				result.Errors.Add("The delimiter must be a comma, semicolon, pipe or tab.");

			var definitions = SplitCsv(template.DefinitionKeysCsv);
			foreach (var definition in definitions)
			{
				if (definition != RmsDefinitionKeys.NerisIncidentReport && !RmsDefinitionKeys.LockedTypes.ContainsKey(definition))
					result.Errors.Add($"'{definition}' is not an available record definition.");
			}
			var operational = definitions.Count == 0 || definitions.Any(d => d != RmsDefinitionKeys.NerisIncidentReport);
			var incident = definitions.Count == 0 || definitions.Contains(RmsDefinitionKeys.NerisIncidentReport);

			var columns = ParseColumns(template.ColumnsJson);
			if (columns.Count == 0)
				result.Errors.Add("Choose at least one column.");
			if (columns.Count > 80)
				result.Errors.Add("An export carries at most 80 columns.");
			var needsNarrative = false;
			var needsRestricted = false;
			foreach (var column in columns)
			{
				var field = RecordsExportFieldCatalog.Get(column);
				if (field == null)
				{
					result.Errors.Add($"'{column}' is not an export column.");
					continue;
				}
				if (!field.Operational && !incident)
					result.Warnings.Add($"'{column}' applies to NERIS incident reports only and will be empty for the chosen definitions.");
				if (!field.Incident && !operational)
					result.Warnings.Add($"'{column}' applies to operational records only and will be empty for the chosen definitions.");
				if (field.Tier == RmsExportFieldTier.Narrative) needsNarrative = true;
				if (field.Tier == RmsExportFieldTier.Restricted) needsRestricted = true;
			}

			// Tier 2 and Tier 1 columns leave the department's boundary: they need an explicit opt-in, an egress
			// acknowledgement and, for restricted, the author's own restricted grant (plan sections 4.7 and 5.9.2).
			if (needsNarrative && !template.IncludeNarrative)
				result.Errors.Add("Narrative-class columns need 'Include narrative and personal detail' switched on.");
			if (needsRestricted && !template.IncludeRestricted)
				result.Errors.Add("Restricted columns need 'Include restricted sections' switched on.");
			if ((needsNarrative || needsRestricted) && !template.EgressAcknowledgedOn.HasValue)
				result.Errors.Add("Acknowledge that this export sends narrative or restricted content outside Resgrid before saving it.");
			if (needsRestricted && !string.IsNullOrWhiteSpace(userId) && !await _authorization.HasPermissionAsync(userId, departmentId, PermissionTypes.ViewRestrictedRecords))
				result.Errors.Add("Only a member with the restricted-records grant can author an export that carries restricted columns.");

			if ((needsNarrative || needsRestricted) && await _protection.IsEnforcedAsync(departmentId))
				result.Warnings.Add("Advanced Data Protection is enforced: protected columns are withheld (REDACTED) in every run until the export's egress acknowledgement is recorded.");

			return result;
		}

		public async Task<RmsExportTemplate> SaveAsync(int departmentId, string userId, RmsExportTemplate template, bool acknowledgeEgress, CancellationToken cancellationToken = default)
		{
			if (template == null) throw new ArgumentNullException(nameof(template));
			if (!await _authorization.HasPermissionAsync(userId, departmentId, PermissionTypes.ManageRecordReports))
				throw new UnauthorizedAccessException("Managing report exports requires the ManageRecordReports permission.");

			var now = DateTime.UtcNow;
			var existing = string.IsNullOrWhiteSpace(template.RmsExportTemplateId) ? null : await _templates.GetByIdForDepartmentAsync(departmentId, template.RmsExportTemplateId);
			var target = existing ?? new RmsExportTemplate { RmsExportTemplateId = Guid.NewGuid().ToString(), DepartmentId = departmentId, ProtectionId = Guid.NewGuid().ToString(), CreatedOn = now, CreatedByUserId = userId, RowVersion = 0 };

			target.TemplateKey = (template.TemplateKey ?? string.Empty).Trim().ToLowerInvariant();
			target.Name = template.Name?.Trim();
			target.Description = string.IsNullOrWhiteSpace(template.Description) ? null : template.Description.Trim();
			target.Format = template.Format;
			target.Scope = template.Scope;
			target.DefinitionKeysCsv = string.Join(",", SplitCsv(template.DefinitionKeysCsv));
			target.ColumnsJson = JsonConvert.SerializeObject(ParseColumns(template.ColumnsJson));
			target.IncludeNarrative = template.IncludeNarrative;
			target.IncludeRestricted = template.IncludeRestricted;
			target.FileNameTemplate = string.IsNullOrWhiteSpace(template.FileNameTemplate) ? null : template.FileNameTemplate.Trim();
			target.IncludeHeader = template.IncludeHeader;
			target.Delimiter = string.IsNullOrEmpty(template.Delimiter) ? "," : template.Delimiter;
			target.ScheduleKind = template.ScheduleKind;
			target.ScheduleHourLocal = template.ScheduleHourLocal;
			target.ScheduleDayOfWeek = template.ScheduleDayOfWeek;
			target.ScheduleDayOfMonth = template.ScheduleDayOfMonth;
			target.WindowDays = template.WindowDays;
			target.IsEnabled = template.IsEnabled;
			target.ModifiedOn = now;
			target.ModifiedByUserId = userId;

			// The acknowledgement is a recorded decision by a named member; it is never carried over silently
			// when the content the export carries widens.
			var carriesSensitive = ParseColumns(target.ColumnsJson).Select(RecordsExportFieldCatalog.Get).Any(f => f != null && f.Tier != RmsExportFieldTier.Safe);
			if (acknowledgeEgress && carriesSensitive)
			{
				target.EgressAcknowledgedOn = now;
				target.EgressAcknowledgedByUserId = userId;
			}
			else if (!carriesSensitive || !target.IncludeNarrative && !target.IncludeRestricted)
			{
				target.EgressAcknowledgedOn = null;
				target.EgressAcknowledgedByUserId = null;
			}

			var validation = await ValidateAsync(departmentId, userId, target);
			if (!validation.IsValid)
				throw new ArgumentException(string.Join(" ", validation.Errors));

			target.NextRunOn = (RmsExportScheduleKind)target.ScheduleKind == RmsExportScheduleKind.None || !target.IsEnabled
				? null
				: await ComputeNextRunAsync(departmentId, target, now);
			target.RowVersion += 1;

			_unitOfWork.CreateOrGetConnection();
			try
			{
				if (existing == null) await _templates.InsertAsync(target, cancellationToken, true);
				else await _templates.UpdateAsync(target, cancellationToken, true);
				await AuditAsync(departmentId, userId, null, RmsAccessAuditAction.Admin, existing == null ? "Export template created" : "Export template updated",
					new { target.RmsExportTemplateId, target.TemplateKey, target.Format, target.Scope, target.IncludeNarrative, target.IncludeRestricted, target.EgressAcknowledgedOn }, cancellationToken);
				_unitOfWork.CommitChanges();
			}
			catch { _unitOfWork.DiscardChanges(); throw; }

			return target;
		}

		public async Task<bool> DeleteAsync(int departmentId, string userId, string templateId, CancellationToken cancellationToken = default)
		{
			if (!await _authorization.HasPermissionAsync(userId, departmentId, PermissionTypes.ManageRecordReports))
				throw new UnauthorizedAccessException("Managing report exports requires the ManageRecordReports permission.");
			var template = await _templates.GetByIdForDepartmentAsync(departmentId, templateId);
			if (template == null)
				return false;

			var now = DateTime.UtcNow;
			template.DeletedOn = now;
			template.IsEnabled = false;
			template.NextRunOn = null;
			template.ModifiedOn = now;
			template.ModifiedByUserId = userId;
			template.RowVersion += 1;

			_unitOfWork.CreateOrGetConnection();
			try
			{
				await _templates.UpdateAsync(template, cancellationToken, true);
				await AuditAsync(departmentId, userId, null, RmsAccessAuditAction.Admin, "Export template deleted", new { template.RmsExportTemplateId, template.TemplateKey }, cancellationToken);
				_unitOfWork.CommitChanges();
			}
			catch { _unitOfWork.DiscardChanges(); throw; }
			return true;
		}

		#endregion

		#region Render

		public async Task<RmsExportRun> RenderAsync(int departmentId, RmsExportTemplate template, RecordsExportRequest request, CancellationToken cancellationToken = default)
		{
			return await RenderCoreAsync(departmentId, template, request ?? new RecordsExportRequest(), true, cancellationToken);
		}

		public async Task<RmsExportRun> ResolveForWorkflowAsync(int departmentId, string templateId, string recordId, RmsRecordKind? recordKind, string scheduledRunId, string workflowRunId, CancellationToken cancellationToken = default)
		{
			var template = await _templates.GetByIdForDepartmentAsync(departmentId, templateId);
			if (template == null || template.DeletedOn.HasValue)
				throw new InvalidOperationException("The export template named by this step no longer exists.");

			var request = new RecordsExportRequest { Trigger = RmsExportTrigger.Record, WorkflowRunId = workflowRunId, Purpose = "Workflow export " + template.Name };
			if ((RmsExportScope)template.Scope == RmsExportScope.Window)
			{
				// A scheduled run is re-rendered for the step from its recorded window rather than read back from
				// storage: the stored bytes are sealed under ADP and a workload never opens them (plan 5.9.2).
				var stored = string.IsNullOrWhiteSpace(scheduledRunId) ? null : await _runs.GetByIdForDepartmentAsync(departmentId, scheduledRunId);
				if (stored != null)
				{
					request.Trigger = RmsExportTrigger.Scheduled;
					request.WindowStart = stored.WindowStart;
					request.WindowEnd = stored.WindowEnd;
				}
				else
				{
					var window = ScheduleWindow(template, DateTime.UtcNow);
					request.WindowStart = window.start;
					request.WindowEnd = window.end;
				}
				var rendered = await RenderCoreAsync(departmentId, template, request, false, cancellationToken);
				if (stored != null)
					rendered.RmsExportRunId = stored.RmsExportRunId;
				return rendered;
			}

			if (string.IsNullOrWhiteSpace(recordId))
				throw new InvalidOperationException("This export covers the triggering record, but the event named no record.");
			request.RecordId = recordId;
			request.RecordKind = recordKind;
			return await RenderCoreAsync(departmentId, template, request, true, cancellationToken);
		}

		private async Task<RmsExportRun> RenderCoreAsync(int departmentId, RmsExportTemplate template, RecordsExportRequest request, bool store, CancellationToken cancellationToken)
		{
			if (template == null) throw new ArgumentNullException(nameof(template));
			if (template.DepartmentId != departmentId) throw new UnauthorizedAccessException("The template belongs to another department.");
			if (!string.IsNullOrWhiteSpace(request.ActingUserId) && !await _authorization.HasPermissionAsync(request.ActingUserId, departmentId, PermissionTypes.ExportRecords))
				throw new UnauthorizedAccessException("Exporting records requires the ExportRecords permission.");

			var columns = ParseColumns(template.ColumnsJson).Select(RecordsExportFieldCatalog.Get).Where(f => f != null).ToList();
			var definitions = SplitCsv(template.DefinitionKeysCsv);
			var restrictedAllowed = template.IncludeRestricted && (string.IsNullOrWhiteSpace(request.ActingUserId) || await _authorization.HasPermissionAsync(request.ActingUserId, departmentId, PermissionTypes.ViewRestrictedRecords));
			var narrativeAllowed = template.IncludeNarrative;
			var department = await _departments.GetDepartmentByIdAsync(departmentId, false);
			var now = DateTime.UtcNow;

			var sources = await ResolveSourcesAsync(departmentId, template, request, definitions, cancellationToken);
			var rows = new List<Dictionary<string, string>>();
			var redactedFields = new SortedSet<string>(StringComparer.Ordinal);
			var withheldTiers = new SortedSet<string>(StringComparer.Ordinal);
			var names = new NameResolver(_profiles, _groups);
			var exported = new List<(string RecordId, string RevisionId)>();

			foreach (var source in sources)
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (!string.IsNullOrWhiteSpace(request.ActingUserId) && !await _authorization.CanUserViewRecordAsync(request.ActingUserId, source.RecordId, departmentId))
					continue;

				var context = await LoadAsync(departmentId, source, template, cancellationToken);
				if (context == null)
					continue;

				// ADP: the ambient reveal already ran inside the aggregate hydrate. What it withheld is re-tried on the
				// export egress lane only when the template's acknowledgement is recorded; otherwise it stays REDACTED.
				if (context.Protection != null && context.Protection.RedactedFields.Count > 0 && template.EgressAcknowledgedOn.HasValue)
					await RevealForExportAsync(departmentId, context, cancellationToken);
				if (context.Protection != null)
					foreach (var field in context.Protection.RedactedFields)
						redactedFields.Add(field);

				var row = new Dictionary<string, string>(StringComparer.Ordinal);
				foreach (var column in columns)
				{
					string value;
					if (column.Tier == RmsExportFieldTier.Restricted && !restrictedAllowed || column.Tier == RmsExportFieldTier.Narrative && !narrativeAllowed)
					{
						value = ProtectedDataEnvelope.RedactionValue;
						withheldTiers.Add(column.Key);
					}
					else
						value = await RecordsExportRenderer.ValueAsync(column.Key, context, department, names);
					row[column.Key] = value ?? string.Empty;
				}
				rows.Add(row);
				exported.Add((context.RecordId, context.RevisionId));
			}

			var format = (RmsExportFormat)template.Format;
			var bytes = format switch
			{
				RmsExportFormat.Json => RecordsExportRenderer.RenderJson(template, columns, rows, request, now),
				RmsExportFormat.Pdf => RecordsExportRenderer.RenderPdf(_pdf, template, columns, rows, request, department, now),
				_ => RecordsExportRenderer.RenderCsv(template, columns, rows)
			};

			var run = new RmsExportRun
			{
				RmsExportRunId = Guid.NewGuid().ToString(),
				DepartmentId = departmentId,
				ProtectionId = Guid.NewGuid().ToString(),
				TemplateId = template.RmsExportTemplateId,
				TemplateKey = template.TemplateKey,
				Trigger = (int)request.Trigger,
				RecordId = (RmsExportScope)template.Scope == RmsExportScope.TriggeringRecord ? request.RecordId : null,
				WindowStart = request.WindowStart,
				WindowEnd = request.WindowEnd,
				RecordCount = rows.Count,
				FileName = FileName(template, request, now),
				ContentType = format == RmsExportFormat.Json ? "application/json" : format == RmsExportFormat.Pdf ? "application/pdf" : "text/csv",
				ByteSize = bytes.LongLength,
				Checksum = RecordSnapshotSerializer.Checksum(bytes),
				Data = bytes,
				Redacted = redactedFields.Count > 0 || withheldTiers.Count > 0,
				RedactedFieldsJson = redactedFields.Count == 0 && withheldTiers.Count == 0 ? null : JsonConvert.SerializeObject(new { protected_fields = redactedFields, withheld_columns = withheldTiers }),
				GeneratedOn = now,
				GeneratedByUserId = request.ActingUserId,
				WorkflowRunId = request.WorkflowRunId,
				ExpiresOn = now.AddDays(RunRetentionDays)
			};

			if (!store)
				return run;

			// The stored copy is sealed under ADP (RmsExportRuns.Data, catalog v10); the caller keeps the plaintext bytes.
			var stored = JsonConvert.DeserializeObject<RmsExportRun>(JsonConvert.SerializeObject(run));
			_unitOfWork.CreateOrGetConnection();
			try
			{
				await _protection.ProtectExportRunAsync(departmentId, stored, request.ActingUserId, cancellationToken);
				await _runs.InsertAsync(stored, cancellationToken, true);
				foreach (var (recordId, revisionId) in exported)
					await AuditAsync(departmentId, request.ActingUserId, recordId, RmsAccessAuditAction.Export, request.Purpose ?? ("Export " + template.Name),
						new { run.RmsExportRunId, template.TemplateKey, run.Checksum, revisionId, run.Redacted, workflowRunId = request.WorkflowRunId }, cancellationToken);
				_unitOfWork.CommitChanges();
			}
			catch { _unitOfWork.DiscardChanges(); throw; }

			run.IsProtected = stored.IsProtected;
			run.ProtectedCatalogVersion = stored.ProtectedCatalogVersion;
			return run;
		}

		private sealed class Source
		{
			public string RecordId;
			public RmsRecordKind Kind;
		}

		private async Task<List<Source>> ResolveSourcesAsync(int departmentId, RmsExportTemplate template, RecordsExportRequest request, List<string> definitions, CancellationToken cancellationToken)
		{
			var result = new List<Source>();
			if ((RmsExportScope)template.Scope == RmsExportScope.TriggeringRecord)
			{
				if (string.IsNullOrWhiteSpace(request.RecordId))
					return result;
				var kind = request.RecordKind;
				if (kind == null)
				{
					var record = await _recordsRepository.GetByIdForDepartmentAsync(departmentId, request.RecordId);
					kind = record != null ? RmsRecordKind.Operational : RmsRecordKind.IncidentReport;
				}
				result.Add(new Source { RecordId = request.RecordId, Kind = kind.Value });
				return result;
			}

			var end = request.WindowEnd ?? DateTime.UtcNow;
			var start = request.WindowStart ?? end.AddDays(-Math.Max(1, EffectiveWindowDays(template)));
			request.WindowStart = start;
			request.WindowEnd = end;

			var wantOperational = definitions.Count == 0 || definitions.Any(d => d != RmsDefinitionKeys.NerisIncidentReport);
			var wantIncident = definitions.Count == 0 || definitions.Contains(RmsDefinitionKeys.NerisIncidentReport);

			if (wantOperational)
			{
				foreach (var record in (await _recordsRepository.GetFinalizedSinceAsync(departmentId, start)) ?? Enumerable.Empty<RmsOperationalRecord>())
				{
					if (record.FinalizedOn == null || record.FinalizedOn >= end || record.DeletedOn.HasValue || record.PurgedOn.HasValue) continue;
					if (definitions.Count > 0 && !definitions.Contains(record.DefinitionKey)) continue;
					if (RmsLifecycle.IsTerminal((RmsRecordState)record.State)) continue;
					result.Add(new Source { RecordId = record.RmsOperationalRecordId, Kind = RmsRecordKind.Operational });
					if (result.Count >= MaxWindowRecords) return result;
				}
			}

			if (wantIncident)
			{
				var query = new RmsIncidentReportQuery { States = FinalizedStates.ToList(), Skip = 0, Take = 250 };
				for (var skip = 0; skip < MaxWindowRecords; skip += 250)
				{
					query.Skip = skip;
					var page = (await _incidentsRepository.QueryAsync(departmentId, query))?.ToList() ?? new List<RmsIncidentReport>();
					foreach (var report in page)
					{
						if (report.FinalizedOn == null || report.FinalizedOn < start || report.FinalizedOn >= end || report.DeletedOn.HasValue || report.PurgedOn.HasValue) continue;
						result.Add(new Source { RecordId = report.RmsIncidentReportId, Kind = RmsRecordKind.IncidentReport });
						if (result.Count >= MaxWindowRecords) return result;
					}
					if (page.Count < 250) break;
				}
			}

			return result.OrderBy(s => s.RecordId, StringComparer.Ordinal).ToList();
		}

		private async Task<RecordsExportContext> LoadAsync(int departmentId, Source source, RmsExportTemplate template, CancellationToken cancellationToken)
		{
			var definitions = SplitCsv(template.DefinitionKeysCsv);
			if (source.Kind == RmsRecordKind.IncidentReport)
			{
				var aggregate = await _incidents.GetAsync(departmentId, source.RecordId, false);
				if (aggregate?.Report == null || aggregate.Report.DeletedOn.HasValue || aggregate.Report.PurgedOn.HasValue) return null;
				if (definitions.Count > 0 && !definitions.Contains(RmsDefinitionKeys.NerisIncidentReport)) return null;
				return new RecordsExportContext { Incident = aggregate, Protection = aggregate.Protection };
			}

			var record = await _records.GetAsync(departmentId, source.RecordId, false);
			if (record?.Record == null || record.Record.DeletedOn.HasValue || record.Record.PurgedOn.HasValue) return null;
			if (definitions.Count > 0 && !definitions.Contains(record.Record.DefinitionKey)) return null;
			return new RecordsExportContext { Operational = record, Protection = record.Protection };
		}

		private async Task RevealForExportAsync(int departmentId, RecordsExportContext context, CancellationToken cancellationToken)
		{
			try
			{
				context.Protection = context.Incident != null
					? await _protection.RevealForWorkloadAsync(departmentId, context.Incident, RecordsProtectionService.RecordsExportPurpose, cancellationToken)
					: await _protection.RevealForWorkloadAsync(departmentId, context.Operational, RecordsProtectionService.RecordsExportPurpose, cancellationToken);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
			catch (Exception ex)
			{
				Logging.LogException(ex, $"Export egress reveal failed for {context.RecordId}; the protected columns stay withheld.");
			}
		}

		#endregion

		#region Runs and schedule

		public async Task<RmsExportRun> GetRunAsync(int departmentId, string runId, bool includeData)
		{
			var run = includeData ? await _runs.GetWithDataAsync(departmentId, runId) : await _runs.GetByIdForDepartmentAsync(departmentId, runId);
			if (run == null || run.DeletedOn.HasValue || run.ExpiresOn <= DateTime.UtcNow)
				return null;
			(await _protection.RevealExportRunsAsync(departmentId, new[] { run }, includeData)).RequireRevealed("export download");
			return run;
		}

		public async Task<List<RmsExportRun>> GetRunsAsync(int departmentId, string templateId, int take)
		{
			var rows = (await _runs.GetForTemplateAsync(departmentId, templateId, take))?.Where(r => !r.DeletedOn.HasValue).ToList() ?? new List<RmsExportRun>();
			await _protection.RevealExportRunsAsync(departmentId, rows, false);
			return rows;
		}

		public async Task<RecordsExportScheduleSweepResult> RunDueSchedulesAsync(CancellationToken cancellationToken = default)
		{
			var result = new RecordsExportScheduleSweepResult();
			var now = DateTime.UtcNow;
			var due = (await _templates.GetDueAsync(now, 100))?.ToList() ?? new List<RmsExportTemplate>();
			foreach (var template in due)
			{
				cancellationToken.ThrowIfCancellationRequested();
				result.TemplatesEvaluated++;
				try
				{
					var window = ScheduleWindow(template, template.NextRunOn ?? now);
					var run = await RenderCoreAsync(template.DepartmentId, template,
						new RecordsExportRequest { Trigger = RmsExportTrigger.Scheduled, WindowStart = window.start, WindowEnd = window.end, Purpose = "Scheduled export " + template.Name },
						true, cancellationToken);
					result.RunsRendered++;
					result.RunIds.Add(run.RmsExportRunId);

					template.LastRunOn = now;
					template.NextRunOn = await ComputeNextRunAsync(template.DepartmentId, template, now);
					template.ModifiedOn = now;
					template.RowVersion += 1;
					await _templates.UpdateAsync(template, cancellationToken, true);
					await _runs.DeleteExpiredAsync(template.DepartmentId, now, cancellationToken);

					var entry = await _outbox.EnqueueAsync(template.DepartmentId, DomainEventProducers.Records, new DomainEventEnvelope
					{
						EventName = WorkflowTriggerEventType.RecordExportScheduled.ToString(),
						SchemaVersion = 1,
						AggregateType = "RmsExportTemplate",
						AggregateId = template.RmsExportTemplateId,
						AggregateVersion = (int)template.RowVersion,
						Trigger = WorkflowTriggerEventType.RecordExportScheduled,
						Payload = new Dictionary<string, object>
						{
							["record"] = new { id = (string)null, kind = "Export", department_id = template.DepartmentId, state = "Rendered" },
							["export"] = ExportBlock(template, run),
							["protection"] = IncidentReportsService.ProtectionBlock(await _protection.GetCatalogVersionAsync(template.DepartmentId))
						},
						CorrelationId = run.RmsExportRunId,
						OriginClient = RmsOriginClient.System
					}, cancellationToken);
					await _outbox.DispatchAfterCommitAsync(new[] { entry.DomainEventOutboxId }, cancellationToken);
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
				catch (Exception ex)
				{
					result.Errors++;
					Logging.LogException(ex, $"Scheduled export {template.TemplateKey} for department {template.DepartmentId} failed.");
					// Push the schedule forward so one broken template cannot wedge the sweep.
					try
					{
						template.NextRunOn = now.AddHours(1);
						template.ModifiedOn = now;
						template.RowVersion += 1;
						await _templates.UpdateAsync(template, cancellationToken, true);
					}
					catch (Exception inner) { Logging.LogException(inner); }
				}
			}
			return result;
		}

		public Task<int> PurgeExpiredRunsAsync(int departmentId, CancellationToken cancellationToken = default)
			=> _runs.DeleteExpiredAsync(departmentId, DateTime.UtcNow, cancellationToken);

		/// <summary>export.* (trigger 160): the run's identity, window, size and checksum; never the rendered content.</summary>
		public static object ExportBlock(RmsExportTemplate template, RmsExportRun run)
		{
			return new
			{
				run_id = run.RmsExportRunId,
				template_id = template.RmsExportTemplateId,
				template_key = template.TemplateKey,
				template_name = template.Name,
				format = ((RmsExportFormat)template.Format).ToString(),
				scope = ((RmsExportScope)template.Scope).ToString(),
				window_start = run.WindowStart,
				window_end = run.WindowEnd,
				record_count = run.RecordCount,
				file_name = run.FileName,
				content_type = run.ContentType,
				byte_size = run.ByteSize,
				checksum = run.Checksum,
				redacted = run.Redacted,
				generated_on = run.GeneratedOn,
				expires_on = run.ExpiresOn
			};
		}

		#endregion

		#region Schedule helpers

		public static int EffectiveWindowDays(RmsExportTemplate template)
		{
			if (template.WindowDays > 0) return template.WindowDays;
			return (RmsExportScheduleKind)template.ScheduleKind switch
			{
				RmsExportScheduleKind.Weekly => 7,
				RmsExportScheduleKind.Monthly => 31,
				_ => 1
			};
		}

		/// <summary>The finalized-on window a run at <paramref name="runAt"/> covers: the period ending at the run.</summary>
		public static (DateTime start, DateTime end) ScheduleWindow(RmsExportTemplate template, DateTime runAt)
		{
			var end = runAt;
			var start = (RmsExportScheduleKind)template.ScheduleKind == RmsExportScheduleKind.Monthly && template.WindowDays == 0 ? end.AddMonths(-1) : end.AddDays(-EffectiveWindowDays(template));
			return (start, end);
		}

		private async Task<DateTime?> ComputeNextRunAsync(int departmentId, RmsExportTemplate template, DateTime utcNow)
		{
			string timeZone = null;
			try { timeZone = (await _departments.GetDepartmentByIdAsync(departmentId, false))?.TimeZone; }
			catch (Exception ex) { Logging.LogException(ex); }
			return ComputeNextRun(template, utcNow, timeZone);
		}

		/// <summary>Next occurrence strictly after <paramref name="utcNow"/> at the department's local hour.</summary>
		public static DateTime? ComputeNextRun(RmsExportTemplate template, DateTime utcNow, string timeZone)
		{
			var kind = (RmsExportScheduleKind)template.ScheduleKind;
			if (kind == RmsExportScheduleKind.None)
				return null;

			DateTime ToUtc(DateTime local)
			{
				if (string.IsNullOrWhiteSpace(timeZone)) return DateTime.SpecifyKind(local, DateTimeKind.Utc);
				try { return DateTimeHelpers.ConvertToUtc(local, timeZone, true); }
				catch { return DateTime.SpecifyKind(local, DateTimeKind.Utc); }
			}
			DateTime ToLocal(DateTime utc)
			{
				if (string.IsNullOrWhiteSpace(timeZone)) return utc;
				try { return utc + (ToUtc(utc) - utc) * -1; }
				catch { return utc; }
			}

			var localNow = ToLocal(utcNow);
			var candidate = new DateTime(localNow.Year, localNow.Month, localNow.Day, template.ScheduleHourLocal, 0, 0);
			for (var i = 0; i < 400; i++)
			{
				var fits = kind switch
				{
					RmsExportScheduleKind.Daily => true,
					RmsExportScheduleKind.Weekly => (int)candidate.DayOfWeek == template.ScheduleDayOfWeek,
					RmsExportScheduleKind.Monthly => candidate.Day == template.ScheduleDayOfMonth,
					_ => false
				};
				var utc = ToUtc(candidate);
				if (fits && utc > utcNow)
					return utc;
				candidate = candidate.AddDays(1);
			}
			return null;
		}

		#endregion

		#region Small helpers

		public static List<string> ParseColumns(string json)
		{
			if (string.IsNullOrWhiteSpace(json))
				return new List<string>();
			try
			{
				var parsed = JsonConvert.DeserializeObject<List<string>>(json) ?? new List<string>();
				return parsed.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).Distinct(StringComparer.Ordinal).ToList();
			}
			catch (JsonException)
			{
				return json.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(c => c.Trim()).Distinct(StringComparer.Ordinal).ToList();
			}
		}

		public static List<string> SplitCsv(string csv)
			=> string.IsNullOrWhiteSpace(csv) ? new List<string>() : csv.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(v => v.Trim()).Where(v => v.Length > 0).Distinct(StringComparer.Ordinal).ToList();

		public static string FileName(RmsExportTemplate template, RecordsExportRequest request, DateTime now)
		{
			var pattern = string.IsNullOrWhiteSpace(template.FileNameTemplate) ? "{template}-{date}" : template.FileNameTemplate.Trim();
			var name = pattern
				.Replace("{template}", template.TemplateKey ?? "export")
				.Replace("{date}", now.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture))
				.Replace("{record}", string.IsNullOrWhiteSpace(request.RecordId) ? "window" : request.RecordId);
			var safe = new string(name.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.' || c == ' ').ToArray()).Trim();
			if (safe.Length == 0) safe = "export";
			var extension = (RmsExportFormat)template.Format switch { RmsExportFormat.Json => ".json", RmsExportFormat.Pdf => ".pdf", _ => ".csv" };
			return safe.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ? safe : safe + extension;
		}

		private Task AuditAsync(int departmentId, string userId, string recordId, RmsAccessAuditAction action, string purpose, object detail, CancellationToken cancellationToken)
		{
			return _audits.InsertAsync(new RmsAccessAudit
			{
				DepartmentId = departmentId,
				RecordId = recordId,
				Action = (int)action,
				ActorUserId = userId,
				Purpose = purpose,
				OriginClient = (int)(string.IsNullOrWhiteSpace(userId) ? RmsOriginClient.System : RmsOriginClient.Web),
				Successful = true,
				OccurredOn = DateTime.UtcNow,
				DetailJson = detail == null ? null : JsonConvert.SerializeObject(detail)
			}, cancellationToken, true);
		}

		#endregion
	}

	/// <summary>One record as the renderer sees it: either aggregate, plus the ADP outcome for its columns.</summary>
	public sealed class RecordsExportContext
	{
		public RecordAggregate Operational { get; set; }
		public IncidentReportAggregate Incident { get; set; }
		public ProtectedReadResult Protection { get; set; }
		public string RecordId => Operational?.Record?.RmsOperationalRecordId ?? Incident?.Report?.RmsIncidentReportId;
		public string RevisionId => Operational?.Record?.CurrentRevisionId ?? Incident?.Report?.CurrentRevisionId;
	}

	/// <summary>Cached person and group name lookups for one render.</summary>
	public sealed class NameResolver
	{
		private readonly IUserProfileService _profiles;
		private readonly IDepartmentGroupsService _groups;
		private readonly Dictionary<string, string> _people = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<int, string> _groupNames = new Dictionary<int, string>();

		public NameResolver(IUserProfileService profiles, IDepartmentGroupsService groups)
		{
			_profiles = profiles;
			_groups = groups;
		}

		public async Task<string> PersonAsync(string userId)
		{
			if (string.IsNullOrWhiteSpace(userId)) return string.Empty;
			if (_people.TryGetValue(userId, out var cached)) return cached;
			string name;
			try { var profile = await _profiles.GetProfileByUserIdAsync(userId, false); name = profile == null ? userId : $"{profile.FirstName} {profile.LastName}".Trim(); }
			catch (Exception) { name = userId; }
			return _people[userId] = string.IsNullOrWhiteSpace(name) ? userId : name;
		}

		public async Task<string> GroupAsync(int? groupId)
		{
			if (!groupId.HasValue) return string.Empty;
			if (_groupNames.TryGetValue(groupId.Value, out var cached)) return cached;
			string name;
			try { name = (await _groups.GetGroupByIdAsync(groupId.Value, false))?.Name ?? groupId.Value.ToString(CultureInfo.InvariantCulture); }
			catch (Exception) { name = groupId.Value.ToString(CultureInfo.InvariantCulture); }
			return _groupNames[groupId.Value] = name;
		}
	}
}
