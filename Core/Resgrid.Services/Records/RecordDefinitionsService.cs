using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Repositories.Queries;
using Resgrid.Model.Services;

namespace Resgrid.Services.Records
{
	/// <summary>
	/// Department definition management (RMS plan sections 4.1 and 5.4, RMS-1B). A definition has a stable key and
	/// immutable published versions; editing opens a new draft, publishing freezes it (checksum, capability floor,
	/// materialized field rows) and raises trigger 113; retiring stops new Records and raises 114. Locked system
	/// definitions are listed alongside but never editable here. Every write needs ManageRecordDefinitions; publish
	/// and retire also need PublishRecordDefinitions.
	/// </summary>
	public class RecordDefinitionsService : IRecordDefinitionsService
	{
		public const string DefinitionAggregate = "RmsRecordDefinition";
		/// <summary>
		/// Subject/reference types a definition may permit. The incident.* group and "vendor" are the incident-support
		/// subjects (Back Office plan E1): a Record scoped to a command instance, an operational period, a checked-in
		/// participant, a supplied resource, a facility, an external resource request, or a vendor. RMS-1C's EOC, SAR
		/// and Mutual Aid packs need period- and participant-scoped records whether or not the Back Office ships.
		/// </summary>
		private static readonly string[] KnownSubjectTypes =
		{
			"call", "unit", "group", "contact", "person", "checklist", "workorder", "inventory", "none",
			"incidentcommand", "incidentoperationalperiod", "incidentparticipant", "incidentresource", "incidentfacility", "incidentresourcerequest", "vendor"
		};

		private readonly IRmsRecordDefinitionsRepository _definitions;
		private readonly IRmsRecordDefinitionVersionsRepository _versions;
		private readonly IRmsRecordSectionDefinitionsRepository _sections;
		private readonly IRmsRecordFieldDefinitionsRepository _fields;
		private readonly IRmsOperationalRecordsRepository _records;
		private readonly IRmsRecordValuesRepository _values;
		private readonly IRecordTypedValuesService _typedValues;
		private readonly IRecordTemplatePacksService _templates;
		private readonly IRecordsAuthorizationService _authorization;
		private readonly IRecordsProtectionService _protection;
		private readonly IDomainEventOutboxService _outbox;
		private readonly IFeatureToggleService _featureToggles;
		private readonly IRmsAccessAuditsRepository _audits;
		private readonly IUnitOfWork _unitOfWork;

		public RecordDefinitionsService(IRmsRecordDefinitionsRepository definitions, IRmsRecordDefinitionVersionsRepository versions, IRmsRecordSectionDefinitionsRepository sections,
			IRmsRecordFieldDefinitionsRepository fields, IRmsOperationalRecordsRepository records, IRmsRecordValuesRepository values, IRecordTypedValuesService typedValues,
			IRecordTemplatePacksService templates, IRecordsAuthorizationService authorization, IRecordsProtectionService protection, IDomainEventOutboxService outbox,
			IFeatureToggleService featureToggles, IRmsAccessAuditsRepository audits, IUnitOfWork unitOfWork)
		{
			_definitions = definitions;
			_versions = versions;
			_sections = sections;
			_fields = fields;
			_records = records;
			_values = values;
			_typedValues = typedValues;
			_templates = templates;
			_authorization = authorization;
			_protection = protection;
			_outbox = outbox;
			_featureToggles = featureToggles;
			_audits = audits;
			_unitOfWork = unitOfWork;
		}

		// ------------------------------------------------------------------------------------------------
		// Reads
		// ------------------------------------------------------------------------------------------------

		public async Task<List<RecordDefinitionSummary>> ListAsync(int departmentId, bool includeRetired = false)
		{
			var list = RecordDefinitionCatalog.Describe().Select(d => new RecordDefinitionSummary
			{
				Key = d.Key, Name = d.Name, Category = "System", Owner = RmsDefinitionOwner.System.ToString(), Locked = true, PublishedVersion = d.Version,
				LifecyclePreset = d.LifecyclePresetName, MinimumClientCapability = d.MinimumClientCapability, ArtifactStatus = RmsArtifactStatus.Exact.ToString()
			}).ToList();

			var definitions = (await _definitions.GetForDepartmentAsync(departmentId, includeRetired))?.ToList() ?? new List<RmsRecordDefinition>();
			foreach (var definition in definitions.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
			{
				var versions = (await _versions.GetForDefinitionAsync(departmentId, definition.DefinitionKey))?.ToList() ?? new List<RmsRecordDefinitionVersion>();
				var published = versions.FirstOrDefault(v => v.IsPublished && v.Version == definition.CurrentPublishedVersion);
				var draft = versions.Where(v => v.IsDraft).OrderByDescending(v => v.Version).FirstOrDefault();
				var latest = published ?? draft ?? versions.OrderByDescending(v => v.Version).FirstOrDefault();
				var template = definition.TemplateKey == null ? null : _templates.GetTemplate(definition.TemplateKey);
				list.Add(new RecordDefinitionSummary
				{
					Key = definition.DefinitionKey, Name = definition.Name, Category = definition.Category, Owner = RmsDefinitionOwner.Department.ToString(), Locked = false,
					PublishedVersion = published?.Version, DraftVersion = draft?.Version, Retired = definition.IsRetired,
					LifecyclePreset = latest == null ? null : ((RmsLifecyclePreset)latest.LifecyclePreset).ToString(),
					Cardinality = latest == null ? null : ((RmsRecordCardinality)latest.Cardinality).ToString(),
					MinimumClientCapability = published?.MinimumClientCapability ?? latest?.MinimumClientCapability ?? RecordsClientCapabilities.Derive(latest?.Schema),
					TemplateKey = definition.TemplateKey, JurisdictionProfileKey = definition.JurisdictionProfileKey,
					ArtifactStatus = template == null ? RmsArtifactStatus.DepartmentLocal.ToString() : (template.Overlays.TryGetValue(definition.JurisdictionProfileKey ?? "generic", out var overlay) ? overlay.ArtifactStatus : RmsArtifactStatus.Compatible).ToString()
				});
			}
			return list;
		}

		public async Task<List<RmsRecordDefinitionVersion>> GetPublishedAsync(int departmentId)
		{
			var definitions = (await _definitions.GetForDepartmentAsync(departmentId, false))?.ToDictionary(d => d.DefinitionKey, StringComparer.OrdinalIgnoreCase) ?? new Dictionary<string, RmsRecordDefinition>();
			var versions = (await _versions.GetPublishedForDepartmentAsync(departmentId))?.ToList() ?? new List<RmsRecordDefinitionVersion>();
			return versions.Where(v => definitions.TryGetValue(v.DefinitionKey, out var d) && !d.IsRetired && d.CurrentPublishedVersion == v.Version).OrderBy(v => v.DefinitionKey, StringComparer.Ordinal).ToList();
		}

		public async Task<RecordDefinitionAggregate> GetAsync(int departmentId, string definitionKey)
		{
			var definition = await _definitions.GetByKeyAsync(departmentId, RecordDefinitionKeys.NormalizeKey(definitionKey));
			if (definition == null) return null;
			return new RecordDefinitionAggregate
			{
				Definition = definition,
				Versions = (await _versions.GetForDefinitionAsync(departmentId, definition.DefinitionKey))?.OrderBy(v => v.Version).ToList() ?? new List<RmsRecordDefinitionVersion>()
			};
		}

		public Task<RmsRecordDefinitionVersion> GetVersionAsync(int departmentId, string definitionKey, int version)
			=> _versions.GetAsync(departmentId, RecordDefinitionKeys.NormalizeKey(definitionKey), version);

		public Task<RmsRecordDefinitionVersion> GetVersionByIdAsync(int departmentId, string versionId) => _versions.GetByIdForDepartmentAsync(departmentId, versionId);

		public async Task<RmsRecordDefinitionVersion> GetCurrentPublishedAsync(int departmentId, string definitionKey)
		{
			var definition = await _definitions.GetByKeyAsync(departmentId, RecordDefinitionKeys.NormalizeKey(definitionKey));
			if (definition == null || definition.IsRetired || !definition.CurrentPublishedVersion.HasValue) return null;
			var version = await _versions.GetAsync(departmentId, definition.DefinitionKey, definition.CurrentPublishedVersion.Value);
			return version != null && version.IsPublished ? version : null;
		}

		// ------------------------------------------------------------------------------------------------
		// Authoring
		// ------------------------------------------------------------------------------------------------

		public async Task<RecordDefinitionAggregate> CreateAsync(int departmentId, string userId, RecordDefinitionCreateInput input, CancellationToken cancellationToken = default)
		{
			if (input == null) throw new ArgumentNullException(nameof(input));
			await RequireManageAsync(userId, departmentId);
			var key = RecordDefinitionKeys.NormalizeKey(input.DefinitionKey);
			if (!RecordDefinitionKeys.IsValidDefinitionKey(key))
				throw new ArgumentException("A definition key uses lower-case letters, digits, dots and dashes, starts with a letter and never uses the reserved 'system.' prefix.", nameof(input));
			if (await _definitions.GetByKeyAsync(departmentId, key) != null)
				throw new ArgumentException($"A definition with key '{key}' already exists.", nameof(input));
			if (string.IsNullOrWhiteSpace(input.Name))
				throw new ArgumentException("A definition name is required.", nameof(input));

			var draft = new RecordDefinitionDraftInput { Name = input.Name.Trim(), Category = input.Category?.Trim() };
			string templateKey = null, profileKey = null;
			if (!string.IsNullOrWhiteSpace(input.TemplateKey))
			{
				var rendering = await _templates.RenderAsync(input.TemplateKey, input.JurisdictionProfileKey ?? "generic", input.Locale);
				if (rendering == null) throw new ArgumentException($"'{input.TemplateKey}' is not a product template.", nameof(input));
				templateKey = rendering.Template.Key;
				profileKey = rendering.ProfileKey;
				draft.Schema = rendering.Schema;
				draft.LifecyclePreset = rendering.Template.LifecyclePreset;
				draft.Cardinality = rendering.Template.Cardinality;
				draft.Numbering = new RecordDefinitionNumbering { Prefix = rendering.Template.NumberPrefix, PerIncidentSequence = rendering.Template.PerIncidentSequence };
				draft.PermittedSubjectTypes = rendering.Template.PermittedSubjectTypes;
				draft.Classification = rendering.Template.Classification;
				draft.RetentionYears = rendering.Template.RetentionYears;
				draft.ClientSurface = rendering.Template.ClientSurface;
				draft.Category = draft.Category ?? rendering.Template.Category;
				draft.Description = rendering.Template.Description;
			}
			else if (!string.IsNullOrWhiteSpace(input.CloneFromDefinitionKey))
			{
				var source = await GetAsync(departmentId, input.CloneFromDefinitionKey);
				var sourceVersion = source?.Published ?? source?.Latest;
				if (sourceVersion == null) throw new ArgumentException($"'{input.CloneFromDefinitionKey}' has no version to clone.", nameof(input));
				draft = ToDraftInput(sourceVersion, source.Definition);
				draft.Name = input.Name.Trim();
				draft.Category = input.Category?.Trim() ?? source.Definition.Category;
				templateKey = source.Definition.TemplateKey;
				profileKey = source.Definition.JurisdictionProfileKey;
			}
			else
			{
				// A blank definition starts with one editable section so the first draft validates; the designer replaces it.
				draft.Numbering = new RecordDefinitionNumbering { Prefix = DerivePrefix(key) };
				draft.Schema = StarterSchema();
			}

			var validation = await ValidateAsync(departmentId, draft);
			if (!validation.IsValid)
				throw new ArgumentException(string.Join(" ", validation.Issues.Where(i => i.Severity == "error").Select(i => i.Message)));

			var now = DateTime.UtcNow;
			var definition = new RmsRecordDefinition
			{
				RmsRecordDefinitionId = Guid.NewGuid().ToString(),
				DepartmentId = departmentId,
				ProtectionId = Guid.NewGuid().ToString(),
				DefinitionKey = key,
				Owner = (int)RmsDefinitionOwner.Department,
				Name = draft.Name,
				Category = draft.Category,
				Description = draft.Description,
				TemplateKey = templateKey,
				TemplatePackVersion = templateKey == null ? (int?)null : 1,
				JurisdictionProfileKey = profileKey,
				PermittedSubjectTypes = draft.PermittedSubjectTypes,
				LatestVersion = 1,
				CreatedOn = now, CreatedByUserId = userId, ModifiedOn = now, ModifiedByUserId = userId, RowVersion = 1
			};
			var version = NewVersion(definition, 1, draft, userId, now);

			await InTransactionAsync(async () =>
			{
				await _definitions.InsertAsync(definition, cancellationToken, true);
				await _versions.InsertAsync(version, cancellationToken, true);
				await AuditAsync(departmentId, userId, definition, version, "Create definition", cancellationToken);
			});
			return await GetAsync(departmentId, key);
		}

		public async Task<RmsRecordDefinitionVersion> OpenDraftAsync(int departmentId, string userId, string definitionKey, CancellationToken cancellationToken = default)
		{
			await RequireManageAsync(userId, departmentId);
			var aggregate = await GetAsync(departmentId, definitionKey) ?? throw new ArgumentException($"'{definitionKey}' is not a department definition.", nameof(definitionKey));
			if (aggregate.Definition.IsRetired) throw new InvalidOperationException("A retired definition cannot open a new draft.");
			if (aggregate.Draft != null) throw new InvalidOperationException($"Draft v{aggregate.Draft.Version} is already open; edit, publish or delete it first.");
			if (aggregate.Draft != null) return aggregate.Draft;
			var source = aggregate.Published ?? aggregate.Latest ?? throw new InvalidOperationException("The definition has no version to draft from.");

			var now = DateTime.UtcNow;
			var draft = NewVersion(aggregate.Definition, aggregate.Definition.LatestVersion + 1, ToDraftInput(source, aggregate.Definition), userId, now);
			await InTransactionAsync(async () =>
			{
				await _versions.InsertAsync(draft, cancellationToken, true);
				aggregate.Definition.LatestVersion = draft.Version;
				aggregate.Definition.ModifiedOn = now; aggregate.Definition.ModifiedByUserId = userId; aggregate.Definition.RowVersion += 1;
				await _definitions.UpdateAsync(aggregate.Definition, cancellationToken, true);
				await AuditAsync(departmentId, userId, aggregate.Definition, draft, "Open draft version", cancellationToken);
			});
			return draft;
		}

		public async Task<RmsRecordDefinitionVersion> SaveDraftAsync(int departmentId, string userId, string definitionKey, int version, long expectedRowVersion, RecordDefinitionDraftInput input, CancellationToken cancellationToken = default)
		{
			if (input == null) throw new ArgumentNullException(nameof(input));
			await RequireManageAsync(userId, departmentId);
			var aggregate = await GetAsync(departmentId, definitionKey) ?? throw new ArgumentException($"'{definitionKey}' is not a department definition.", nameof(definitionKey));
			var row = aggregate.Versions.FirstOrDefault(v => v.Version == version) ?? throw new ArgumentException($"Version {version} does not exist.", nameof(version));
			if (!row.IsDraft) throw new InvalidOperationException("A published version is immutable; open a new draft to change it.");
			if (row.RowVersion != expectedRowVersion) throw new RecordConcurrencyException(row.RmsRecordDefinitionVersionId, expectedRowVersion, row.RowVersion);

			var validation = await ValidateAsync(departmentId, input);
			ApplyTemplateFloors(validation, aggregate.Definition.TemplateKey, input.Schema);
			if (!validation.IsValid)
				throw new ArgumentException(string.Join(" ", validation.Issues.Where(i => i.Severity == "error").Select(i => i.Message)));

			var now = DateTime.UtcNow;
			Apply(row, input, validation.MinimumClientCapability);
			row.ModifiedOn = now; row.ModifiedByUserId = userId; row.RowVersion += 1;
			aggregate.Definition.Name = string.IsNullOrWhiteSpace(input.Name) ? aggregate.Definition.Name : input.Name.Trim();
			aggregate.Definition.Category = input.Category?.Trim() ?? aggregate.Definition.Category;
			aggregate.Definition.Description = input.Description ?? aggregate.Definition.Description;
			aggregate.Definition.PermittedSubjectTypes = input.PermittedSubjectTypes ?? aggregate.Definition.PermittedSubjectTypes;
			aggregate.Definition.ModifiedOn = now; aggregate.Definition.ModifiedByUserId = userId; aggregate.Definition.RowVersion += 1;

			await InTransactionAsync(async () =>
			{
				await _versions.UpdateAsync(row, cancellationToken, true);
				await _definitions.UpdateAsync(aggregate.Definition, cancellationToken, true);
				await AuditAsync(departmentId, userId, aggregate.Definition, row, "Save draft version", cancellationToken);
			});
			return row;
		}

		public async Task<bool> DeleteDraftAsync(int departmentId, string userId, string definitionKey, int version, CancellationToken cancellationToken = default)
		{
			await RequireManageAsync(userId, departmentId);
			var aggregate = await GetAsync(departmentId, definitionKey);
			var row = aggregate?.Versions.FirstOrDefault(v => v.Version == version);
			if (row == null) return false;
			if (!row.IsDraft) throw new InvalidOperationException("Only an unused draft can be deleted.");
			if (await _values.CountRecordsOnVersionAsync(departmentId, row.RmsRecordDefinitionVersionId, false) > 0)
				throw new InvalidOperationException("Records already reference this draft version; it cannot be deleted.");

			await InTransactionAsync(async () =>
			{
				await _versions.DeleteAsync(row, cancellationToken);
				if (aggregate.Versions.Count == 1)
				{
					aggregate.Definition.DeletedOn = DateTime.UtcNow;
					await _definitions.UpdateAsync(aggregate.Definition, cancellationToken, true);
				}
				await AuditAsync(departmentId, userId, aggregate.Definition, row, "Delete draft version", cancellationToken);
			});
			return true;
		}

		// ------------------------------------------------------------------------------------------------
		// Validation
		// ------------------------------------------------------------------------------------------------

		public Task<RecordDefinitionValidation> ValidateAsync(int departmentId, RecordDefinitionDraftInput input)
		{
			var result = new RecordDefinitionValidation();
			if (input == null) { result.Issues.Add(RecordDefinitionIssue.Error("", "missing", "Nothing to validate.")); return Task.FromResult(result); }
			var schema = input.Schema ?? new RecordDefinitionSchema();
			var issues = result.Issues;

			// Name/category/description live on the definition; a null name on a draft save means "unchanged" (ToDraftInput, publish re-validation).
			if (input.Name != null && string.IsNullOrWhiteSpace(input.Name)) issues.Add(RecordDefinitionIssue.Error("name", "required", "A definition name is required."));
			if (input.Name?.Length > 200) issues.Add(RecordDefinitionIssue.Error("name", "too_long", "The name is limited to 200 characters."));
			if (!Enum.IsDefined(typeof(RmsLifecyclePreset), input.LifecyclePreset)) issues.Add(RecordDefinitionIssue.Error("lifecyclePreset", "unknown", "Choose one of the governed lifecycle presets."));
			if (!Enum.IsDefined(typeof(RmsRecordCardinality), input.Cardinality)) issues.Add(RecordDefinitionIssue.Error("cardinality", "unknown", "Choose one of the governed cardinality rules."));
			// A cardinality rule other than MultiplePerCall is keyed on the Call, so a definition that cannot name
			// one has written a rule that never fires (plan 5.2.1).
			else if (input.Cardinality != RmsRecordCardinality.MultiplePerCall
				&& !(input.PermittedSubjectTypes ?? string.Empty).Split(',').Select(t => t.Trim()).Contains("call", StringComparer.OrdinalIgnoreCase))
				issues.Add(RecordDefinitionIssue.Error("cardinality", "no_call_subject", $"{input.Cardinality} is enforced per Call; add the 'call' subject or choose MultiplePerCall."));
			if (input.LifecyclePreset == RmsLifecyclePreset.ApprovalAcknowledgement && input.ApproverRoleIds != null && input.ReviewerRoleIds != null && input.ApproverRoleIds.Count > 0 && input.ReviewerRoleIds.Count > 0 && input.ApproverRoleIds.All(input.ReviewerRoleIds.Contains))
				issues.Add(RecordDefinitionIssue.Warning("approverRoleIds", "same_roles", "Approvers and reviewers are the same roles; the approver may still never be the author."));
			if (input.ReviewDueHours.HasValue && (input.ReviewDueHours <= 0 || input.ReviewDueHours > 24 * 365)) issues.Add(RecordDefinitionIssue.Error("reviewDueHours", "out_of_range", "Review due hours must be between 1 and 8760."));
			if (input.ApproveDueHours.HasValue && (input.ApproveDueHours <= 0 || input.ApproveDueHours > 24 * 365)) issues.Add(RecordDefinitionIssue.Error("approveDueHours", "out_of_range", "Approve due hours must be between 1 and 8760."));
			if (input.RetentionYears.HasValue && (input.RetentionYears < 0 || input.RetentionYears > 100)) issues.Add(RecordDefinitionIssue.Error("retentionYears", "out_of_range", "Retention is 0 (permanent) to 100 years."));
			foreach (var subject in (input.PermittedSubjectTypes ?? string.Empty).Split(',').Select(s => s.Trim().ToLowerInvariant()).Where(s => s.Length > 0))
				if (!KnownSubjectTypes.Contains(subject)) issues.Add(RecordDefinitionIssue.Error("permittedSubjectTypes", "unknown_subject", $"'{subject}' is not a supported subject type."));

			var numbering = input.Numbering ?? new RecordDefinitionNumbering();
			if (string.IsNullOrWhiteSpace(numbering.Prefix) || numbering.Prefix.Length < 2 || numbering.Prefix.Length > 6 || !numbering.Prefix.All(c => char.IsLetterOrDigit(c) && !char.IsLower(c)))
				issues.Add(RecordDefinitionIssue.Error("numbering.prefix", "bad_prefix", "The number prefix is 2 to 6 upper-case letters or digits."));
			if (numbering.SequenceWidth < 3 || numbering.SequenceWidth > 8) issues.Add(RecordDefinitionIssue.Error("numbering.sequenceWidth", "out_of_range", "The sequence width is 3 to 8 digits."));
			// Incident-scoped numbering only reaches its scope through a Call; without the subject every Record falls
			// back to the department sequence, which is a surprise rather than an error worth blocking a publish on.
			if (numbering.PerIncidentSequence && !(input.PermittedSubjectTypes ?? string.Empty).Split(',').Select(s => s.Trim()).Contains("call", StringComparer.OrdinalIgnoreCase))
				issues.Add(RecordDefinitionIssue.Warning("numbering.perIncidentSequence", "no_call_subject", "Incident-scoped numbering needs the 'call' subject; Records without a Call use the department sequence."));
			if (!Enum.IsDefined(typeof(RmsNumberAssignment), numbering.Assignment)) issues.Add(RecordDefinitionIssue.Error("numbering.assignment", "unknown", "Numbers are assigned OnFinalize or OnCreate."));

			if (schema.Sections.Count == 0) issues.Add(RecordDefinitionIssue.Error("schema", "no_sections", "A definition needs at least one section."));
			if (schema.Sections.Count > RecordDefinitionSchema.MaxSections) issues.Add(RecordDefinitionIssue.Error("schema", "too_many_sections", $"At most {RecordDefinitionSchema.MaxSections} sections."));
			var sectionKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var fieldKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var fieldSections = new Dictionary<string, RecordSectionSchema>(StringComparer.OrdinalIgnoreCase);
			foreach (var section in schema.Sections)
			{
				var path = "schema.sections." + (section.Key ?? "?");
				if (!RecordDefinitionKeys.IsValidMemberKey(section.Key)) issues.Add(RecordDefinitionIssue.Error(path, "bad_key", $"Section key '{section.Key}' is invalid (letters, digits, underscore, dash; starts with a letter)."));
				else if (!sectionKeys.Add(section.Key)) issues.Add(RecordDefinitionIssue.Error(path, "duplicate_key", $"Section key '{section.Key}' is used twice."));
				if (string.IsNullOrWhiteSpace(section.Label)) issues.Add(RecordDefinitionIssue.Error(path, "label_required", $"Section '{section.Key}' needs a label."));
				if (section.Fields.Count == 0) issues.Add(RecordDefinitionIssue.Error(path, "no_fields", $"Section '{section.Key}' has no fields."));
				if (section.Fields.Count > RecordDefinitionSchema.MaxFieldsPerSection) issues.Add(RecordDefinitionIssue.Error(path, "too_many_fields", $"Section '{section.Key}' exceeds {RecordDefinitionSchema.MaxFieldsPerSection} fields."));
				if (section.Repeating && section.MaxRows.HasValue && (section.MaxRows < 1 || section.MaxRows > RecordTypedValuesService.MaxRowsPerSection)) issues.Add(RecordDefinitionIssue.Error(path, "bad_rows", $"Repeating sections allow 1 to {RecordTypedValuesService.MaxRowsPerSection} rows."));
				if (section.MinRows.HasValue && section.MaxRows.HasValue && section.MinRows > section.MaxRows) issues.Add(RecordDefinitionIssue.Error(path, "bad_rows", "MinRows exceeds MaxRows."));
				foreach (var field in section.Fields)
				{
					var fieldPath = path + "." + (field.Key ?? "?");
					if (!RecordDefinitionKeys.IsValidMemberKey(field.Key)) issues.Add(RecordDefinitionIssue.Error(fieldPath, "bad_key", $"Field key '{field.Key}' is invalid."));
					else if (!fieldKeys.Add(field.Key)) issues.Add(RecordDefinitionIssue.Error(fieldPath, "duplicate_key", $"Field key '{field.Key}' is used twice; keys are unique across the definition."));
					else fieldSections[field.Key] = section;
					if (string.IsNullOrWhiteSpace(field.Label)) issues.Add(RecordDefinitionIssue.Error(fieldPath, "label_required", $"Field '{field.Key}' needs a label."));
					if (!Enum.IsDefined(typeof(RmsFieldType), field.Type)) issues.Add(RecordDefinitionIssue.Error(fieldPath, "unknown_type", $"Field '{field.Key}' has an unsupported type."));
					if ((field.Type == RmsFieldType.SingleSelect || field.Type == RmsFieldType.MultiSelect))
					{
						if (field.Options.Count == 0) issues.Add(RecordDefinitionIssue.Error(fieldPath, "no_options", $"'{field.Key}' needs at least one option."));
						if (field.Options.Count > RecordDefinitionSchema.MaxOptions) issues.Add(RecordDefinitionIssue.Error(fieldPath, "too_many_options", $"'{field.Key}' exceeds {RecordDefinitionSchema.MaxOptions} options."));
						var optionKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
						foreach (var option in field.Options)
						{
							if (string.IsNullOrWhiteSpace(option.Key) || option.Key.Length > 64) issues.Add(RecordDefinitionIssue.Error(fieldPath, "bad_option", $"'{field.Key}' has an option without a key."));
							else if (!optionKeys.Add(option.Key)) issues.Add(RecordDefinitionIssue.Error(fieldPath, "duplicate_option", $"'{field.Key}' repeats option '{option.Key}'."));
						}
					}
					if (field.Type == RmsFieldType.Quantity && (string.IsNullOrWhiteSpace(field.UnitFamily) || !RmsUnits.CanonicalByFamily.ContainsKey(field.UnitFamily)))
						issues.Add(RecordDefinitionIssue.Error(fieldPath, "bad_unit_family", $"Quantity field '{field.Key}' needs a unit family ({string.Join(", ", RmsUnits.CanonicalByFamily.Keys)})."));
					if (field.Type == RmsFieldType.Quantity && !string.IsNullOrWhiteSpace(field.DefaultUnit) && RmsUnits.Find(field.DefaultUnit)?.Family != field.UnitFamily)
						issues.Add(RecordDefinitionIssue.Error(fieldPath, "bad_unit", $"'{field.DefaultUnit}' is not a {field.UnitFamily} unit."));
					if (field.Type == RmsFieldType.Currency && !string.IsNullOrWhiteSpace(field.DefaultCurrency) && !RmsCurrencies.IsSupported(field.DefaultCurrency))
						issues.Add(RecordDefinitionIssue.Error(fieldPath, "bad_currency", $"'{field.DefaultCurrency}' is not a supported currency."));
					if (field.Min.HasValue && field.Max.HasValue && field.Min > field.Max) issues.Add(RecordDefinitionIssue.Error(fieldPath, "bad_range", $"'{field.Key}' has Min above Max."));
					if (field.MaxLength.HasValue && (field.MaxLength < 1 || field.MaxLength > RecordTypedValuesService.MaxLongText)) issues.Add(RecordDefinitionIssue.Error(fieldPath, "bad_length", $"'{field.Key}' MaxLength is out of range."));
					if (field.Classification < input.Classification) issues.Add(RecordDefinitionIssue.Error(fieldPath, "classification_loosened", $"'{field.Key}' cannot be less classified than the definition ({input.Classification})."));

					// Capability flags are restricted by type and protection (plan 4.1): an author cannot declare a
					// protected or restricted value safe for search, Workflow or export, and long text never indexes.
					if (field.Classification != RmsFieldClassification.Standard && (field.Searchable || field.WorkflowExposed || field.Groupable || field.Aggregatable))
						issues.Add(RecordDefinitionIssue.Error(fieldPath, "protected_exposed", $"'{field.Key}' is {field.Classification}; it cannot be searchable, groupable, aggregatable or Workflow-exposed."));
					if (field.Aggregatable && !IsNumeric(field.Type)) issues.Add(RecordDefinitionIssue.Error(fieldPath, "not_aggregatable", $"'{field.Key}' ({field.Type}) cannot be aggregated; only numeric, currency, quantity and duration fields can."));
					if (field.Groupable && !IsGroupable(field.Type)) issues.Add(RecordDefinitionIssue.Error(fieldPath, "not_groupable", $"'{field.Key}' ({field.Type}) cannot group a report."));
					if ((field.Sortable || field.Filterable) && (field.Type == RmsFieldType.LongText || field.Type == RmsFieldType.Attachment || field.Type == RmsFieldType.Signature))
						issues.Add(RecordDefinitionIssue.Error(fieldPath, "not_filterable", $"'{field.Key}' ({field.Type}) cannot be filtered or sorted."));
					if (field.Searchable && (field.Type == RmsFieldType.Attachment || field.Type == RmsFieldType.Signature))
						issues.Add(RecordDefinitionIssue.Error(fieldPath, "not_searchable", $"'{field.Key}' ({field.Type}) cannot be searched."));
				}
			}

			// Rules: bounded operators, references to existing scalar fields, depth, and no cycles.
			var graph = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
			foreach (var section in schema.Sections)
			{
				foreach (var rule in section.Rules)
					ValidateRule(issues, "schema.sections." + section.Key + ".rules", rule, schema, fieldSections, section, true, graph, "section:" + section.Key);
				foreach (var field in section.Fields)
					foreach (var rule in field.Rules)
						ValidateRule(issues, "schema.sections." + section.Key + "." + field.Key + ".rules", rule, schema, fieldSections, section, false, graph, field.Key);
			}
			var cycle = FindCycle(graph);
			if (cycle != null) issues.Add(RecordDefinitionIssue.Error("schema.rules", "cycle", "Rules form a cycle: " + cycle + ". A rule may not depend on a field whose own visibility depends on it."));

			result.MinimumClientCapability = RecordsClientCapabilities.Derive(schema);
			if (result.MinimumClientCapability == RecordsClientCapabilities.Packs)
				result.Issues.Add(RecordDefinitionIssue.Warning("schema", "capability", "This version uses RMS-1C field types (currency, quantity, country/subdivision or module references); clients below records.v1c fail closed for authoring."));
			return Task.FromResult(result);
		}

		private static void ValidateRule(List<RecordDefinitionIssue> issues, string path, RecordRuleSchema rule, RecordDefinitionSchema schema, Dictionary<string, RecordSectionSchema> fieldSections, RecordSectionSchema owner, bool isSection, Dictionary<string, HashSet<string>> graph, string target)
		{
			if (rule == null) return;
			if (isSection && rule.Effect != RmsRuleEffect.Show) issues.Add(RecordDefinitionIssue.Error(path, "bad_effect", "A section rule can only control visibility."));
			if (rule.Condition == null) { issues.Add(RecordDefinitionIssue.Error(path, "no_condition", "A rule needs a condition.")); return; }
			ValidateCondition(issues, path, rule.Condition, schema, fieldSections, 0, owner, isSection);
			if (!graph.TryGetValue(target, out var deps)) graph[target] = deps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (var key in rule.Condition.ReferencedFieldKeys())
			{
				deps.Add(key);
				// A field's visibility also depends on its section's visibility.
				if (fieldSections.TryGetValue(key, out var depSection) && depSection.Rules.Count > 0) deps.Add("section:" + depSection.Key);
			}
			if (!isSection && owner.Rules.Count > 0)
			{
				if (!graph.TryGetValue(target, out var own)) graph[target] = own = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				own.Add("section:" + owner.Key);
			}
		}

		private static void ValidateCondition(List<RecordDefinitionIssue> issues, string path, RecordConditionSchema condition, RecordDefinitionSchema schema, Dictionary<string, RecordSectionSchema> fieldSections, int depth, RecordSectionSchema owner = null, bool isSection = false)
		{
			if (depth > RecordDefinitionSchema.MaxRuleDepth) { issues.Add(RecordDefinitionIssue.Error(path, "too_deep", $"Rules nest at most {RecordDefinitionSchema.MaxRuleDepth} levels.")); return; }
			if (!Enum.IsDefined(typeof(RmsRuleOperator), condition.Operator)) { issues.Add(RecordDefinitionIssue.Error(path, "bad_operator", "Unsupported rule operator.")); return; }
			if (condition.Operator == RmsRuleOperator.And || condition.Operator == RmsRuleOperator.Or)
			{
				if (condition.Conditions == null || condition.Conditions.Count == 0) issues.Add(RecordDefinitionIssue.Error(path, "empty_composition", "AND/OR needs child conditions."));
				foreach (var child in condition.Conditions ?? new List<RecordConditionSchema>()) ValidateCondition(issues, path, child, schema, fieldSections, depth + 1, owner, isSection);
				return;
			}
			var field = schema.FindField(condition.FieldKey);
			if (field == null) { issues.Add(RecordDefinitionIssue.Error(path, "unknown_field", $"Rule references unknown field '{condition.FieldKey}'.")); return; }
			// A field inside a repeating section may be referenced only by a field rule of the same section: the rule then
			// evaluates per row against that row's cell. Section rules and other sections see scalars only; row counts and
			// cross-row aggregates stay deferred (plan section 4.1).
			if (fieldSections.TryGetValue(field.Key, out var section) && section.Repeating && (isSection || owner == null || !string.Equals(owner.Key, section.Key, StringComparison.OrdinalIgnoreCase)))
				issues.Add(RecordDefinitionIssue.Error(path, "repeating_reference", $"Rules cannot reference '{condition.FieldKey}' inside repeating section '{section.Key}' from outside that section; only a field of the same section may, and it evaluates per row (count-of-rows conditions are deferred)."));
			switch (condition.Operator)
			{
				case RmsRuleOperator.Equals: case RmsRuleOperator.NotEquals:
					if (condition.Value == null) issues.Add(RecordDefinitionIssue.Error(path, "no_value", "Equals/NotEquals needs a value."));
					if ((field.Type == RmsFieldType.SingleSelect || field.Type == RmsFieldType.MultiSelect) && condition.Value != null && !field.Options.Any(o => string.Equals(o.Key, condition.Value, StringComparison.OrdinalIgnoreCase)))
						issues.Add(RecordDefinitionIssue.Error(path, "unknown_option", $"'{condition.Value}' is not an option of '{field.Key}'."));
					break;
				case RmsRuleOperator.InSet: case RmsRuleOperator.NotInSet:
					if (condition.Values == null || condition.Values.Count == 0) issues.Add(RecordDefinitionIssue.Error(path, "no_values", "InSet/NotInSet needs values."));
					break;
				case RmsRuleOperator.InRange:
					if (!IsNumeric(field.Type) && field.Type != RmsFieldType.Date && field.Type != RmsFieldType.DateTime) issues.Add(RecordDefinitionIssue.Error(path, "not_range", $"'{field.Key}' does not support range conditions."));
					if (!condition.Min.HasValue && !condition.Max.HasValue && !condition.MinDate.HasValue && !condition.MaxDate.HasValue) issues.Add(RecordDefinitionIssue.Error(path, "no_range", "InRange needs a minimum or maximum."));
					break;
			}
		}

		/// <summary>Depth-first cycle search over the rule dependency graph; returns the cycle path or null.</summary>
		public static string FindCycle(Dictionary<string, HashSet<string>> graph)
		{
			var state = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
			var stack = new List<string>();
			string Visit(string node)
			{
				if (state.TryGetValue(node, out var s))
				{
					if (s == 1) return string.Join(" -> ", stack.SkipWhile(n => !string.Equals(n, node, StringComparison.OrdinalIgnoreCase)).Concat(new[] { node }));
					return null;
				}
				state[node] = 1; stack.Add(node);
				if (graph.TryGetValue(node, out var deps))
					foreach (var dep in deps)
					{
						var found = Visit(dep);
						if (found != null) return found;
					}
				stack.RemoveAt(stack.Count - 1); state[node] = 2;
				return null;
			}
			foreach (var node in graph.Keys.ToList())
			{
				var found = Visit(node);
				if (found != null) return found;
			}
			return null;
		}

		public static bool IsNumeric(RmsFieldType type) => type == RmsFieldType.Integer || type == RmsFieldType.Decimal || type == RmsFieldType.Currency || type == RmsFieldType.Quantity || type == RmsFieldType.Duration;
		public static bool IsGroupable(RmsFieldType type) => type == RmsFieldType.SingleSelect || type == RmsFieldType.Boolean || type == RmsFieldType.ShortText || type == RmsFieldType.Unit || type == RmsFieldType.Group || type == RmsFieldType.Person || type == RmsFieldType.Contact || type == RmsFieldType.Date || type == RmsFieldType.CountrySubdivision || type == RmsFieldType.Integer;

		// ------------------------------------------------------------------------------------------------
		// Publish, retire, history, diff, impact
		// ------------------------------------------------------------------------------------------------

		public async Task<RecordDefinitionImpactPreview> ImpactPreviewAsync(int departmentId, string definitionKey, int version)
		{
			var aggregate = await GetAsync(departmentId, definitionKey) ?? throw new ArgumentException($"'{definitionKey}' is not a department definition.", nameof(definitionKey));
			var row = aggregate.Versions.FirstOrDefault(v => v.Version == version) ?? throw new ArgumentException($"Version {version} does not exist.", nameof(version));
			var validation = await ValidateAsync(departmentId, ToDraftInput(row, aggregate.Definition));
			var schema = row.Schema;
			var preview = new RecordDefinitionImpactPreview
			{
				DefinitionKey = aggregate.Definition.DefinitionKey, Version = version, Issues = validation.Issues,
				MinimumClientCapability = validation.MinimumClientCapability,
				FieldTypesUsed = schema.AllFields().Select(f => f.Type.ToString()).Distinct().OrderBy(t => t, StringComparer.Ordinal).ToList(),
				UsesRepeatingGroups = schema.Sections.Any(s => s.Repeating),
				CurrentPublishedVersion = aggregate.Definition.CurrentPublishedVersion
			};
			var published = aggregate.Published;
			if (published != null && published.Version != version)
			{
				preview.OpenDraftsOnCurrentVersion = await _values.CountRecordsOnVersionAsync(departmentId, published.RmsRecordDefinitionVersionId, true);
				preview.FinalizedRecordsOnEarlierVersions = await _values.CountRecordsOnVersionAsync(departmentId, published.RmsRecordDefinitionVersionId, false) - preview.OpenDraftsOnCurrentVersion;
				var diff = DiffVersions(aggregate.Definition.DefinitionKey, published, row);
				preview.BreakingChange = diff.Breaking;
			}
			var surface = row.ClientSurface;
			foreach (var (app, flag, eligible) in new[]
			{
				("Responder", FeatureFlagKeys.RecordsFieldResponder, surface.Responder),
				("Unit", FeatureFlagKeys.RecordsFieldUnit, surface.Unit),
				("IncidentCommand", FeatureFlagKeys.RecordsFieldIncidentCommand, surface.IncidentCommand),
				("Dispatch", FeatureFlagKeys.RecordsFieldDispatch, surface.Dispatch)
			})
			{
				bool enabled;
				try { enabled = await _featureToggles.IsEnabledAsync(flag, departmentId); } catch (Exception) { enabled = false; }
				preview.Clients.Add(new RecordDefinitionClientImpact
				{
					App = app, Enabled = enabled, EligibleOnSurface = eligible, RequiredCapability = validation.MinimumClientCapability, ClientsBelowFloor = null,
					Message = !enabled ? "Field Records are off for this app; Web is the authoring path." : !eligible ? "This version is not offered to this app." :
						validation.MinimumClientCapability == RecordsClientCapabilities.Packs ? "Clients below records.v1c fail closed for authoring and keep read-only access; Web always renders it." : "Clients at records.v1b or later can author this version."
				});
			}
			return preview;
		}

		public async Task<RmsRecordDefinitionVersion> PublishAsync(int departmentId, string userId, string definitionKey, int version, long expectedRowVersion, CancellationToken cancellationToken = default)
		{
			await RequirePublishAsync(userId, departmentId);
			var aggregate = await GetAsync(departmentId, definitionKey) ?? throw new ArgumentException($"'{definitionKey}' is not a department definition.", nameof(definitionKey));
			if (aggregate.Definition.IsRetired) throw new InvalidOperationException("A retired definition cannot publish.");
			var row = aggregate.Versions.FirstOrDefault(v => v.Version == version) ?? throw new ArgumentException($"Version {version} does not exist.", nameof(version));
			if (!row.IsDraft) throw new InvalidOperationException("Only a draft version can be published.");
			if (row.RowVersion != expectedRowVersion) throw new RecordConcurrencyException(row.RmsRecordDefinitionVersionId, expectedRowVersion, row.RowVersion);

			var validation = await ValidateAsync(departmentId, ToDraftInput(row, aggregate.Definition));
			ApplyTemplateFloors(validation, aggregate.Definition.TemplateKey, row.Schema);
			if (!validation.IsValid) throw new ArgumentException(string.Join(" ", validation.Issues.Where(i => i.Severity == "error").Select(i => i.Message)));

			var now = DateTime.UtcNow;
			var schema = row.Schema;
			row.SchemaJson = RecordDefinitionSchema.Serialize(schema);
			row.SchemaChecksum = RecordSnapshotSerializer.Checksum(schema.Canonical());
			row.MinimumClientCapability = validation.MinimumClientCapability;
			row.State = (int)RmsDefinitionVersionState.Published;
			row.PublishedOn = now; row.PublishedByUserId = userId;
			row.ModifiedOn = now; row.ModifiedByUserId = userId; row.RowVersion += 1;

			var previous = aggregate.Published;
			var outboxIds = new List<long>();
			await InTransactionAsync(async () =>
			{
				await _versions.UpdateAsync(row, cancellationToken, true);
				await MaterializeAsync(row, schema, now, cancellationToken);
				if (previous != null && previous.RmsRecordDefinitionVersionId != row.RmsRecordDefinitionVersionId)
				{
					// The previous published version stays readable for its Records; only the pointer moves.
					previous.ModifiedOn = now; previous.ModifiedByUserId = userId; previous.RowVersion += 1;
					await _versions.UpdateAsync(previous, cancellationToken, true);
				}
				aggregate.Definition.CurrentPublishedVersion = row.Version;
				aggregate.Definition.LatestVersion = Math.Max(aggregate.Definition.LatestVersion, row.Version);
				aggregate.Definition.ModifiedOn = now; aggregate.Definition.ModifiedByUserId = userId; aggregate.Definition.RowVersion += 1;
				await _definitions.UpdateAsync(aggregate.Definition, cancellationToken, true);
				outboxIds.Add((await EnqueueAsync(aggregate.Definition, row, WorkflowTriggerEventType.RecordDefinitionPublished, previous?.Version, null, cancellationToken)).DomainEventOutboxId);
				await AuditAsync(departmentId, userId, aggregate.Definition, row, "Publish definition version", cancellationToken);
			});
			await _outbox.DispatchAfterCommitAsync(outboxIds, cancellationToken);
			return row;
		}

		public async Task<RmsRecordDefinition> RetireAsync(int departmentId, string userId, string definitionKey, long expectedRowVersion, string reason, CancellationToken cancellationToken = default)
		{
			await RequirePublishAsync(userId, departmentId);
			var aggregate = await GetAsync(departmentId, definitionKey) ?? throw new ArgumentException($"'{definitionKey}' is not a department definition.", nameof(definitionKey));
			var definition = aggregate.Definition;
			if (definition.IsRetired) return definition;
			if (definition.RowVersion != expectedRowVersion) throw new RecordConcurrencyException(definition.RmsRecordDefinitionId, expectedRowVersion, definition.RowVersion);
			if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A reason is required to retire a definition.", nameof(reason));

			var now = DateTime.UtcNow;
			var published = aggregate.Published;
			var outboxIds = new List<long>();
			await InTransactionAsync(async () =>
			{
				definition.IsRetired = true; definition.RetiredOn = now; definition.RetiredByUserId = userId; definition.RetiredReason = reason.Trim();
				definition.ModifiedOn = now; definition.ModifiedByUserId = userId; definition.RowVersion += 1;
				await _definitions.UpdateAsync(definition, cancellationToken, true);
				foreach (var version in aggregate.Versions.Where(v => v.IsPublished))
				{
					version.State = (int)RmsDefinitionVersionState.Retired; version.RetiredOn = now; version.RetiredByUserId = userId;
					version.ModifiedOn = now; version.ModifiedByUserId = userId; version.RowVersion += 1;
					await _versions.UpdateAsync(version, cancellationToken, true);
				}
				outboxIds.Add((await EnqueueAsync(definition, published ?? aggregate.Latest, WorkflowTriggerEventType.RecordDefinitionRetired, null, reason, cancellationToken)).DomainEventOutboxId);
				await AuditAsync(departmentId, userId, definition, published, "Retire definition", cancellationToken);
			});
			await _outbox.DispatchAfterCommitAsync(outboxIds, cancellationToken);
			return definition;
		}

		public async Task<List<RmsRecordDefinitionVersion>> HistoryAsync(int departmentId, string definitionKey)
			=> (await GetAsync(departmentId, definitionKey))?.Versions.OrderByDescending(v => v.Version).ToList() ?? new List<RmsRecordDefinitionVersion>();

		public async Task<RecordDefinitionDiff> DiffAsync(int departmentId, string definitionKey, int fromVersion, int toVersion)
		{
			var aggregate = await GetAsync(departmentId, definitionKey) ?? throw new ArgumentException($"'{definitionKey}' is not a department definition.", nameof(definitionKey));
			var from = aggregate.Versions.FirstOrDefault(v => v.Version == fromVersion) ?? throw new ArgumentException($"Version {fromVersion} does not exist.");
			var to = aggregate.Versions.FirstOrDefault(v => v.Version == toVersion) ?? throw new ArgumentException($"Version {toVersion} does not exist.");
			return DiffVersions(aggregate.Definition.DefinitionKey, from, to);
		}

		/// <summary>Safe diff: keys, types, requiredness, classification, options, rules and policies. Removing or retyping a field is breaking.</summary>
		public static RecordDefinitionDiff DiffVersions(string definitionKey, RmsRecordDefinitionVersion from, RmsRecordDefinitionVersion to)
		{
			var diff = new RecordDefinitionDiff { DefinitionKey = definitionKey, FromVersion = from.Version, ToVersion = to.Version };
			DiffSchemas(diff, from.Schema, to.Schema);
			if (from.LifecyclePreset != to.LifecyclePreset) diff.Entries.Add(new RecordDefinitionDiffEntry { Kind = "policy", Change = "changed", Key = "lifecyclePreset", Detail = $"{(RmsLifecyclePreset)from.LifecyclePreset} -> {(RmsLifecyclePreset)to.LifecyclePreset}", Breaking = false });
			// Tightening cardinality is breaking: Records that were legal under the old rule already exist, and the
			// new rule refuses the next one on a Call that already has them.
			if (from.Cardinality != to.Cardinality) diff.Entries.Add(new RecordDefinitionDiffEntry { Kind = "policy", Change = "changed", Key = "cardinality", Detail = $"{(RmsRecordCardinality)from.Cardinality} -> {(RmsRecordCardinality)to.Cardinality}", Breaking = (RmsRecordCardinality)to.Cardinality != RmsRecordCardinality.MultiplePerCall });
			if (!string.Equals(from.NumberingJson, to.NumberingJson, StringComparison.Ordinal)) diff.Entries.Add(new RecordDefinitionDiffEntry { Kind = "policy", Change = "changed", Key = "numbering", Detail = "Numbering policy changed; issued numbers never change.", Breaking = false });
			if (from.RetentionYears != to.RetentionYears) diff.Entries.Add(new RecordDefinitionDiffEntry { Kind = "policy", Change = "changed", Key = "retentionYears", Detail = $"{from.RetentionYears?.ToString() ?? "class default"} -> {to.RetentionYears?.ToString() ?? "class default"}", Breaking = false });
			if (from.Classification != to.Classification) diff.Entries.Add(new RecordDefinitionDiffEntry { Kind = "policy", Change = "changed", Key = "classification", Detail = $"{(RmsFieldClassification)from.Classification} -> {(RmsFieldClassification)to.Classification}", Breaking = to.Classification < from.Classification });
			if (!string.Equals(from.ReviewerRoleIds ?? "", to.ReviewerRoleIds ?? "", StringComparison.Ordinal) || !string.Equals(from.ApproverRoleIds ?? "", to.ApproverRoleIds ?? "", StringComparison.Ordinal))
				diff.Entries.Add(new RecordDefinitionDiffEntry { Kind = "policy", Change = "changed", Key = "roles", Detail = "Reviewer/approver roles changed.", Breaking = false });
			return diff;
		}

		public static void DiffSchemas(RecordDefinitionDiff diff, RecordDefinitionSchema from, RecordDefinitionSchema to)
		{
			from = from ?? new RecordDefinitionSchema(); to = to ?? new RecordDefinitionSchema();
			foreach (var section in from.Sections.Where(s => to.FindSection(s.Key) == null))
				diff.Entries.Add(new RecordDefinitionDiffEntry { Kind = "section", Change = "removed", Key = section.Key, Detail = section.Label, Breaking = true });
			foreach (var section in to.Sections.Where(s => from.FindSection(s.Key) == null))
				diff.Entries.Add(new RecordDefinitionDiffEntry { Kind = "section", Change = "added", Key = section.Key, Detail = section.Label, Breaking = false });
			foreach (var section in to.Sections)
			{
				var old = from.FindSection(section.Key);
				if (old == null) continue;
				if (old.Repeating != section.Repeating) diff.Entries.Add(new RecordDefinitionDiffEntry { Kind = "section", Change = "changed", Key = section.Key, Detail = "Repeating changed", Breaking = true });
				else if (!string.Equals(old.Label, section.Label, StringComparison.Ordinal)) diff.Entries.Add(new RecordDefinitionDiffEntry { Kind = "section", Change = "changed", Key = section.Key, Detail = $"Label '{old.Label}' -> '{section.Label}'", Breaking = false });
			}
			foreach (var field in from.AllFields().Where(f => to.FindField(f.Key) == null))
				diff.Entries.Add(new RecordDefinitionDiffEntry { Kind = "field", Change = "removed", Key = field.Key, Detail = field.Label, Breaking = true });
			foreach (var field in to.AllFields().Where(f => from.FindField(f.Key) == null))
				diff.Entries.Add(new RecordDefinitionDiffEntry { Kind = "field", Change = "added", Key = field.Key, Detail = $"{field.Label} ({field.Type})" + (field.Required || field.RequiredToFinalize ? ", required" : ""), Breaking = field.Required || field.RequiredToFinalize });
			foreach (var field in to.AllFields())
			{
				var old = from.FindField(field.Key);
				if (old == null) continue;
				var details = new List<string>(); var breaking = false;
				if (old.Type != field.Type) { details.Add($"type {old.Type} -> {field.Type}"); breaking = true; }
				if (!string.Equals(old.Label, field.Label, StringComparison.Ordinal)) details.Add($"label '{old.Label}' -> '{field.Label}'");
				if ((old.Required || old.RequiredToFinalize) != (field.Required || field.RequiredToFinalize)) { details.Add(field.Required || field.RequiredToFinalize ? "now required" : "no longer required"); breaking |= field.Required || field.RequiredToFinalize; }
				if (old.Classification != field.Classification) { details.Add($"classification {old.Classification} -> {field.Classification}"); breaking |= field.Classification < old.Classification; }
				var removedOptions = old.Options.Select(o => o.Key).Except(field.Options.Select(o => o.Key), StringComparer.OrdinalIgnoreCase).ToList();
				var addedOptions = field.Options.Select(o => o.Key).Except(old.Options.Select(o => o.Key), StringComparer.OrdinalIgnoreCase).ToList();
				if (removedOptions.Count > 0) { details.Add("options removed: " + string.Join(", ", removedOptions)); breaking = true; }
				if (addedOptions.Count > 0) details.Add("options added: " + string.Join(", ", addedOptions));
				if (JsonConvert.SerializeObject(old.Rules) != JsonConvert.SerializeObject(field.Rules)) details.Add("rules changed");
				if (old.Searchable != field.Searchable || old.Filterable != field.Filterable || old.Sortable != field.Sortable || old.Groupable != field.Groupable || old.Aggregatable != field.Aggregatable || old.WorkflowExposed != field.WorkflowExposed || old.Exportable != field.Exportable) details.Add("capability flags changed");
				if (old.Min != field.Min || old.Max != field.Max || old.MaxLength != field.MaxLength) details.Add("constraints changed");
				if (details.Count > 0) diff.Entries.Add(new RecordDefinitionDiffEntry { Kind = "field", Change = "changed", Key = field.Key, Detail = string.Join("; ", details), Breaking = breaking });
			}
		}

		public async Task<RecordDefinitionMigrationResult> MigrateDraftsAsync(int departmentId, string userId, string definitionKey, int fromVersion, int toVersion, List<RecordDefinitionFieldMapping> mapping, bool preview, CancellationToken cancellationToken = default)
		{
			await RequireManageAsync(userId, departmentId);
			var aggregate = await GetAsync(departmentId, definitionKey) ?? throw new ArgumentException($"'{definitionKey}' is not a department definition.", nameof(definitionKey));
			var from = aggregate.Versions.FirstOrDefault(v => v.Version == fromVersion) ?? throw new ArgumentException($"Version {fromVersion} does not exist.");
			var to = aggregate.Versions.FirstOrDefault(v => v.Version == toVersion) ?? throw new ArgumentException($"Version {toVersion} does not exist.");
			if (!to.IsPublished) throw new InvalidOperationException("Drafts can only migrate to a published version.");
			if (toVersion <= fromVersion) throw new InvalidOperationException("Drafts migrate forward only.");

			var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (var field in from.Schema.AllFields()) if (to.Schema.FindField(field.Key) != null) map[field.Key] = field.Key;
			foreach (var entry in mapping ?? new List<RecordDefinitionFieldMapping>())
				if (!string.IsNullOrWhiteSpace(entry?.FromFieldKey) && !string.IsNullOrWhiteSpace(entry.ToFieldKey)) map[entry.FromFieldKey] = entry.ToFieldKey;

			var result = new RecordDefinitionMigrationResult();
			foreach (var pair in map)
			{
				var source = from.Schema.FindField(pair.Key); var target = to.Schema.FindField(pair.Value);
				if (source == null || target == null || source.Type != target.Type) result.UnmappedFieldKeys.Add(pair.Key);
			}
			foreach (var field in from.Schema.AllFields()) if (!map.ContainsKey(field.Key)) result.UnmappedFieldKeys.Add(field.Key);
			result.UnmappedFieldKeys = result.UnmappedFieldKeys.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

			var drafts = (await _records.GetByDefinitionVersionAsync(departmentId, aggregate.Definition.DefinitionKey, fromVersion, new[] { (int)RmsRecordState.Draft, (int)RmsRecordState.Returned }))?.ToList() ?? new List<RmsOperationalRecord>();
			foreach (var record in drafts)
			{
				if (record.AmendsRevisionId != null) { result.Skipped++; result.SkippedRecordIds.Add(record.RmsOperationalRecordId); continue; }
				if (preview) { result.Migrated++; continue; }
				var values = await _typedValues.HydrateAsync(departmentId, record.RmsOperationalRecordId, null, from, true);
				var inputs = values.ToInputs().Where(i => map.ContainsKey(i.FieldKey)).Select(i => { var target = map[i.FieldKey]; i.FieldKey = target; i.SectionKey = to.Schema.SectionOf(target)?.Key; return i; }).Where(i => i.SectionKey != null).ToList();
				var validation = await _typedValues.ValidateAsync(departmentId, to, inputs, false);
				if (!validation.IsValid) { result.Skipped++; result.SkippedRecordIds.Add(record.RmsOperationalRecordId); continue; }
				await InTransactionAsync(async () =>
				{
					await _typedValues.SaveDraftValuesAsync(departmentId, userId, record.RmsOperationalRecordId, to, inputs, cancellationToken);
					record.DefinitionVersion = toVersion;
					record.LifecyclePreset = to.LifecyclePreset;
					record.ModifiedOn = DateTime.UtcNow; record.ModifiedByUserId = userId; record.RowVersion += 1;
					await _records.UpdateAsync(record, cancellationToken, true);
				});
				result.Migrated++;
			}
			if (!preview) await AuditAsync(departmentId, userId, aggregate.Definition, to, $"Migrate {result.Migrated} draft(s) from v{fromVersion}", cancellationToken);
			return result;
		}

		// ------------------------------------------------------------------------------------------------
		// Helpers
		// ------------------------------------------------------------------------------------------------

		private static RmsRecordDefinitionVersion NewVersion(RmsRecordDefinition definition, int number, RecordDefinitionDraftInput input, string userId, DateTime now)
		{
			var version = new RmsRecordDefinitionVersion
			{
				RmsRecordDefinitionVersionId = Guid.NewGuid().ToString(),
				DepartmentId = definition.DepartmentId,
				ProtectionId = Guid.NewGuid().ToString(),
				RmsRecordDefinitionId = definition.RmsRecordDefinitionId,
				DefinitionKey = definition.DefinitionKey,
				Version = number,
				State = (int)RmsDefinitionVersionState.Draft,
				CreatedOn = now, CreatedByUserId = userId, ModifiedOn = now, ModifiedByUserId = userId, RowVersion = 1
			};
			Apply(version, input, RecordsClientCapabilities.Derive(input.Schema));
			return version;
		}

		private static void Apply(RmsRecordDefinitionVersion version, RecordDefinitionDraftInput input, string capability)
		{
			version.LifecyclePreset = (int)input.LifecyclePreset;
			version.Cardinality = (int)input.Cardinality;
			version.ReviewerRoleIds = string.Join(",", (input.ReviewerRoleIds ?? new List<int>()).Distinct());
			version.ApproverRoleIds = string.Join(",", (input.ApproverRoleIds ?? new List<int>()).Distinct());
			version.ReviewDueHours = input.ReviewDueHours;
			version.ApproveDueHours = input.ApproveDueHours;
			version.RequireAuthorAttestation = input.RequireAuthorAttestation;
			version.Numbering = input.Numbering ?? new RecordDefinitionNumbering();
			version.RetentionYears = input.RetentionYears;
			version.Classification = (int)input.Classification;
			version.Schema = Normalize(input.Schema ?? new RecordDefinitionSchema());
			version.ClientSurface = input.ClientSurface ?? new RecordDefinitionClientSurface();
			version.MigrationMapJson = JsonConvert.SerializeObject(input.MigrationMap ?? new List<RecordDefinitionFieldMapping>());
			version.ChangeNotes = input.ChangeNotes;
			version.MinimumClientCapability = capability;
		}

		private static RecordDefinitionSchema Normalize(RecordDefinitionSchema schema)
		{
			foreach (var section in schema.Sections)
			{
				section.Key = RecordDefinitionKeys.NormalizeKey(section.Key);
				foreach (var field in section.Fields)
				{
					field.Key = RecordDefinitionKeys.NormalizeKey(field.Key);
					field.Options ??= new List<RecordOptionSchema>();
					field.Rules ??= new List<RecordRuleSchema>();
					foreach (var option in field.Options) option.Key = option.Key?.Trim();
				}
				section.Rules ??= new List<RecordRuleSchema>();
			}
			return schema;
		}

		public static RecordDefinitionDraftInput ToDraftInput(RmsRecordDefinitionVersion version, RmsRecordDefinition definition = null)
		{
			return new RecordDefinitionDraftInput
			{
				Name = definition?.Name, Category = definition?.Category, Description = definition?.Description, PermittedSubjectTypes = definition?.PermittedSubjectTypes,
				LifecyclePreset = (RmsLifecyclePreset)version.LifecyclePreset, Cardinality = (RmsRecordCardinality)version.Cardinality,
				ReviewerRoleIds = ParseIds(version.ReviewerRoleIds), ApproverRoleIds = ParseIds(version.ApproverRoleIds),
				ReviewDueHours = version.ReviewDueHours, ApproveDueHours = version.ApproveDueHours, RequireAuthorAttestation = version.RequireAuthorAttestation,
				Numbering = version.Numbering, RetentionYears = version.RetentionYears, Classification = (RmsFieldClassification)version.Classification,
				Schema = RecordDefinitionSchema.Parse(version.SchemaJson), ClientSurface = version.ClientSurface,
				MigrationMap = string.IsNullOrWhiteSpace(version.MigrationMapJson) ? new List<RecordDefinitionFieldMapping>() : JsonConvert.DeserializeObject<List<RecordDefinitionFieldMapping>>(version.MigrationMapJson) ?? new List<RecordDefinitionFieldMapping>(),
				ChangeNotes = version.ChangeNotes
			};
		}

		public static List<int> ParseIds(string csv) => (csv ?? string.Empty).Split(',').Select(s => int.TryParse(s.Trim(), out var id) ? id : (int?)null).Where(i => i.HasValue).Select(i => i.Value).ToList();

		/// <summary>
		/// A department clone of a pack template may raise a field's classification but never lower it below the pack's
		/// protected-data policy floor (RMS-1C); the floor is what keeps the safe projections safe.
		/// </summary>
		public void ApplyTemplateFloors(RecordDefinitionValidation validation, string templateKey, RecordDefinitionSchema schema)
		{
			if (validation == null || string.IsNullOrWhiteSpace(templateKey) || schema == null) return;
			var template = _templates.GetTemplate(templateKey);
			if (template == null) return;
			foreach (var field in schema.AllFields())
			{
				var floor = template.FloorFor(field.Key);
				if (floor.HasValue && field.Classification < floor.Value)
					validation.Issues.Add(RecordDefinitionIssue.Error("schema.sections." + (schema.SectionOf(field.Key)?.Key ?? "?") + "." + field.Key, "template_floor",
						$"'{field.Key}' carries the {template.PackKey} policy floor {floor.Value}; a department may raise it, not lower it."));
			}
		}

		public static RecordDefinitionSchema StarterSchema() => new RecordDefinitionSchema
		{
			Sections = new List<RecordSectionSchema>
			{
				new RecordSectionSchema
				{
					Key = "details", Label = "Details",
					Fields = new List<RecordFieldSchema>
					{
						new RecordFieldSchema { Key = "summary", Label = "Summary", Type = RmsFieldType.ShortText, Required = true, Searchable = true, Filterable = true, Sortable = true, WorkflowExposed = true },
						new RecordFieldSchema { Key = "notes", Label = "Notes", Type = RmsFieldType.LongText }
					}
				}
			}
		};

		private static string DerivePrefix(string key)
		{
			var letters = new string(key.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
			return letters.Length >= 3 ? letters.Substring(0, 3) : (letters + "REC").Substring(0, 3);
		}

		private async Task MaterializeAsync(RmsRecordDefinitionVersion version, RecordDefinitionSchema schema, DateTime now, CancellationToken cancellationToken)
		{
			await _fields.DeleteForVersionAsync(version.DepartmentId, version.RmsRecordDefinitionVersionId, cancellationToken);
			await _sections.DeleteForVersionAsync(version.DepartmentId, version.RmsRecordDefinitionVersionId, cancellationToken);
			var sectionOrdinal = 0;
			foreach (var section in schema.Sections)
			{
				await _sections.InsertAsync(new RmsRecordSectionDefinition
				{
					RmsRecordSectionDefinitionId = Guid.NewGuid().ToString(), DepartmentId = version.DepartmentId, ProtectionId = Guid.NewGuid().ToString(),
					RmsRecordDefinitionVersionId = version.RmsRecordDefinitionVersionId, DefinitionKey = version.DefinitionKey, DefinitionVersion = version.Version,
					SectionKey = section.Key, Label = section.Label, Help = section.Help, Ordinal = sectionOrdinal++, IsRepeating = section.Repeating, MinRows = section.MinRows, MaxRows = section.MaxRows,
					RulesJson = section.Rules.Count == 0 ? null : JsonConvert.SerializeObject(section.Rules), CreatedOn = now, ModifiedOn = now, RowVersion = 1
				}, cancellationToken, true);
				var fieldOrdinal = 0;
				foreach (var field in section.Fields)
					await _fields.InsertAsync(new RmsRecordFieldDefinition
					{
						RmsRecordFieldDefinitionId = Guid.NewGuid().ToString(), DepartmentId = version.DepartmentId, ProtectionId = Guid.NewGuid().ToString(),
						RmsRecordDefinitionVersionId = version.RmsRecordDefinitionVersionId, DefinitionKey = version.DefinitionKey, DefinitionVersion = version.Version,
						SectionKey = section.Key, FieldKey = field.Key, Label = field.Label, DataType = (int)field.Type, Ordinal = fieldOrdinal++, Required = field.Required, RequiredToFinalize = field.RequiredToFinalize,
						Classification = (int)field.Classification, ReferenceType = field.ReferenceType, Searchable = field.Searchable, Filterable = field.Filterable, Sortable = field.Sortable, Groupable = field.Groupable,
						Aggregatable = field.Aggregatable, WorkflowExposed = field.WorkflowExposed, Exportable = field.Exportable,
						ConstraintsJson = JsonConvert.SerializeObject(new { field.Min, field.Max, field.MaxLength, field.UnitFamily, field.DefaultUnit, field.FixedUnitLabel, field.DefaultCurrency, Options = field.Options.Select(o => o.Key) }),
						RulesJson = field.Rules.Count == 0 ? null : JsonConvert.SerializeObject(field.Rules), CreatedOn = now, ModifiedOn = now, RowVersion = 1
					}, cancellationToken, true);
			}
		}

		private async Task<DomainEventOutboxEntry> EnqueueAsync(RmsRecordDefinition definition, RmsRecordDefinitionVersion version, WorkflowTriggerEventType trigger, int? previousVersion, string reason, CancellationToken cancellationToken)
		{
			int catalogVersion;
			try { catalogVersion = await _protection.GetCatalogVersionAsync(definition.DepartmentId); } catch (Exception) { catalogVersion = 0; }
			var payload = new Dictionary<string, object>
			{
				["definition"] = DefinitionBlock(definition, version, previousVersion, reason),
				["protection"] = IncidentReportsService.ProtectionBlock(catalogVersion)
			};
			return await _outbox.EnqueueAsync(definition.DepartmentId, DomainEventProducers.Records, new DomainEventEnvelope
			{
				EventName = trigger.ToString(),
				SchemaVersion = 1,
				AggregateType = DefinitionAggregate,
				AggregateId = definition.RmsRecordDefinitionId,
				AggregateVersion = version?.Version ?? definition.LatestVersion,
				Trigger = trigger,
				Payload = payload,
				CorrelationId = definition.RmsRecordDefinitionId,
				OriginClient = RmsOriginClient.Web
			}, cancellationToken);
		}

		/// <summary>The definition.* block (plan section 5.6): stable key, version, category/template lineage and the explicitly exposed field keys. Never field values.</summary>
		public static object DefinitionBlock(RmsRecordDefinition definition, RmsRecordDefinitionVersion version, int? previousVersion, string reason)
		{
			var schema = version?.Schema ?? new RecordDefinitionSchema();
			return new
			{
				id = definition.RmsRecordDefinitionId,
				key = definition.DefinitionKey,
				name = definition.Name,
				category = definition.Category,
				owner = ((RmsDefinitionOwner)definition.Owner).ToString(),
				version = version?.Version,
				previous_version = previousVersion,
				state = version == null ? null : ((RmsDefinitionVersionState)version.State).ToString(),
				lifecycle_preset = version == null ? null : ((RmsLifecyclePreset)version.LifecyclePreset).ToString(),
				cardinality = version == null ? null : ((RmsRecordCardinality)version.Cardinality).ToString(),
				template_key = definition.TemplateKey,
				jurisdiction_profile_key = definition.JurisdictionProfileKey,
				minimum_client_capability = version?.MinimumClientCapability,
				schema_checksum = version?.SchemaChecksum,
				published_on = version?.PublishedOn,
				retired = definition.IsRetired,
				retired_on = definition.RetiredOn,
				reason,
				exposed_field_keys = schema.AllFields().Where(f => f.WorkflowExposed && f.Classification == RmsFieldClassification.Standard).Select(f => f.Key).ToList(),
				section_keys = schema.Sections.Select(s => s.Key).ToList()
			};
		}

		private async Task RequireManageAsync(string userId, int departmentId)
		{
			if (string.IsNullOrWhiteSpace(userId) || !await _authorization.HasPermissionAsync(userId, departmentId, PermissionTypes.ManageRecordDefinitions))
				throw new UnauthorizedAccessException("Managing Record definitions is not authorized.");
		}

		private async Task RequirePublishAsync(string userId, int departmentId)
		{
			await RequireManageAsync(userId, departmentId);
			if (!await _authorization.HasPermissionAsync(userId, departmentId, PermissionTypes.PublishRecordDefinitions))
				throw new UnauthorizedAccessException("Publishing Record definitions is not authorized.");
		}

		private async Task AuditAsync(int departmentId, string userId, RmsRecordDefinition definition, RmsRecordDefinitionVersion version, string purpose, CancellationToken cancellationToken)
		{
			await _audits.InsertAsync(new RmsAccessAudit
			{
				DepartmentId = departmentId,
				RecordId = definition.RmsRecordDefinitionId,
				RevisionId = version?.RmsRecordDefinitionVersionId,
				Action = (int)RmsAccessAuditAction.Admin,
				ActorUserId = userId,
				Purpose = purpose + " " + definition.DefinitionKey + (version == null ? string.Empty : " v" + version.Version),
				OriginClient = (int)RmsOriginClient.Web,
				Successful = true,
				OccurredOn = DateTime.UtcNow,
				CorrelationId = definition.RmsRecordDefinitionId
			}, cancellationToken, true);
		}

		private async Task InTransactionAsync(Func<Task> work)
		{
			_unitOfWork.CreateOrGetConnection();
			try
			{
				await work();
				_unitOfWork.CommitChanges();
			}
			catch
			{
				_unitOfWork.DiscardChanges();
				throw;
			}
		}
	}
}
