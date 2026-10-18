using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Resgrid.Framework;
using Resgrid.Model;
using Resgrid.Model.Onboarding;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services.Onboarding.Writers;
using Resgrid.Services.Records;

namespace Resgrid.Services.Onboarding
{
	/// <summary>The outcome of writing one configuration item.</summary>
	public sealed class ImportConfigurationWriteResult
	{
		public bool Written { get; set; }
		/// <summary>Value-free reason it was not written.</summary>
		public string Code { get; set; }

		public static ImportConfigurationWriteResult Ok() => new ImportConfigurationWriteResult { Written = true };
		public static ImportConfigurationWriteResult Refused(string code) => new ImportConfigurationWriteResult { Code = code };
	}

	/// <summary>
	/// Reads and writes the Phase 2 configuration targets (wizard plan sections 7.5 and 8.1 A) through their owning services,
	/// exactly as the department's own settings screens do: the department record and address, the profile and logo, module
	/// switches, terminology, map centre, zoom and styles, call, document and record numbering, and permissions. Values are the
	/// canonical forms of <see cref="ImportConfigurationCatalog"/>. Nothing here opens a transaction: module switches need
	/// their own, so every write runs outside any chunk transaction.
	/// </summary>
	public class ImportConfigurationTargets
	{
		private readonly IDepartmentsService _departments;
		private readonly IAddressService _addresses;
		private readonly IDepartmentSettingsService _settings;
		private readonly IDepartmentProfileMediaService _profiles;
		private readonly ICallNumberingService _callNumbering;
		private readonly IDocumentNumberingService _documentNumbering;
		private readonly IPermissionsService _permissions;
		private readonly IImportArtifactsRepository _artifacts;
		private readonly IImportArtifactStore _store;
		private readonly IImportArtifactCipher _cipher;
		private readonly IImportKeyService _keys;
		private readonly IRecordsNumberingService _recordsNumbering;
		private readonly IFeatureToggleService _features;

		public ImportConfigurationTargets(IDepartmentsService departments, IAddressService addresses, IDepartmentSettingsService settings,
			IDepartmentProfileMediaService profiles, ICallNumberingService callNumbering, IDocumentNumberingService documentNumbering,
			IPermissionsService permissions, IImportArtifactsRepository artifacts, IImportArtifactStore store, IImportArtifactCipher cipher,
			IImportKeyService keys, IRecordsNumberingService recordsNumbering, IFeatureToggleService features)
		{
			_recordsNumbering = recordsNumbering;
			_features = features;
			_departments = departments;
			_addresses = addresses;
			_settings = settings;
			_profiles = profiles;
			_callNumbering = callNumbering;
			_documentNumbering = documentNumbering;
			_permissions = permissions;
			_artifacts = artifacts;
			_store = store;
			_cipher = cipher;
			_keys = keys;
		}

		/// <summary>The value an item reads back when the department has a logo (the uploaded artifact is not kept as its value).</summary>
		public const string LogoPresent = "present";

		/// <summary>
		/// True while Records is on for the department (the Records Settings screen's own gate): only then does record numbering
		/// read, write or show. Off, the Records group reads nothing and every write is refused (IMP-CONFIG-UNAVAILABLE).
		/// </summary>
		public Task<bool> RecordsAvailableAsync(int departmentId) => _features.IsEnabledAsync(FeatureFlagKeys.RecordsSystem, departmentId);

		// ---- Read ------------------------------------------------------------------------------------------------------

		/// <summary>
		/// The current canonical value of each item (empty = Resgrid's default or not set). Only the sources the items need
		/// are read; <paramref name="fresh"/> bypasses the caches (commit and rollback), the screens use cached values.
		/// </summary>
		public async Task<Dictionary<string, string>> ReadAsync(int departmentId, IEnumerable<ImportConfigurationItem> items, bool fresh,
			CancellationToken cancellationToken = default)
		{
			var wanted = (items ?? ImportConfigurationCatalog.All).ToList();
			var values = new Dictionary<string, string>(StringComparer.Ordinal);
			var groups = new HashSet<string>(wanted.Select(i => i.Group), StringComparer.Ordinal);
			var now = DateTime.UtcNow;

			if (groups.Contains(ImportConfigurationGroups.Department))
			{
				var department = await _departments.GetDepartmentByIdAsync(departmentId, fresh);
				values["department.name"] = department?.Name ?? string.Empty;
				values["department.time_zone"] = TimeZoneValue(department?.TimeZone);
				values["department.use_24_hour_time"] = ImportCanonical.Bool(department?.Use24HourTime ?? false);
				var address = department?.AddressId is int addressId && addressId > 0 ? await _addresses.GetAddressByIdAsync(addressId) : null;
				values["department.address"] = address == null ? string.Empty : AddressValue(address);
			}

			if (groups.Contains(ImportConfigurationGroups.Branding))
			{
				var branding = await _profiles.GetBrandingAsync(departmentId);
				values["branding.short_name"] = branding?.Profile?.ShortName ?? string.Empty;
				values["branding.description"] = branding?.Profile?.Description ?? string.Empty;
				values["branding.phone"] = branding?.Profile?.PhoneNumber ?? string.Empty;
				values["branding.website"] = branding?.Profile?.Website ?? string.Empty;
				values["branding.logo"] = branding?.HasLogo == true ? LogoPresent : string.Empty;
			}

			if (groups.Contains(ImportConfigurationGroups.Modules))
			{
				var modules = await _settings.GetDepartmentModuleSettingsAsync(departmentId, fresh) ?? new DepartmentModuleSettings();
				foreach (var module in ImportConfigurationCatalog.Modules)
					values["modules." + module] = ImportCanonical.Bool(!ModuleDisabled(modules, module));
			}

			if (groups.Contains(ImportConfigurationGroups.Terminology))
			{
				var terminology = await _settings.GetDepartmentTerminologyAsync(departmentId, fresh) ?? new DepartmentTerminologyConfig();
				foreach (var term in DepartmentTerms.All)
					values["terminology." + term.Key] = terminology.TextFor(term.Key) ?? string.Empty;
			}

			if (groups.Contains(ImportConfigurationGroups.Map))
			{
				var center = await _settings.GetBigBoardCenterGpsCoordinatesDepartmentAsync(departmentId);
				values["map.center"] = string.IsNullOrWhiteSpace(center)
					? string.Empty
					: ImportConfigurationCatalog.Canonicalize(ImportConfigurationCatalog.Find("map.center"), center).Value ?? string.Empty;
				var zoom = await _settings.GetBigBoardMapZoomLevelForDepartmentAsync(departmentId);
				values["map.zoom"] = zoom.HasValue ? zoom.Value.ToString(CultureInfo.InvariantCulture) : string.Empty;
				values["map.day_style"] = (await _settings.GetMappingMapStyleAsync(departmentId, fresh)).ToString();
				values["map.night_style"] = (await _settings.GetMappingMapStyleNightAsync(departmentId, fresh)).ToString();
			}

			if (groups.Contains(ImportConfigurationGroups.Numbering))
			{
				var calls = await _settings.GetCallNumberingConfigAsync(departmentId, fresh) ?? new CallNumberingConfig();
				values["numbering.calls.pattern"] = CallNumberFormat.EffectivePattern(calls);
				values["numbering.calls.width"] = ImportCanonical.Int(CallNumberFormat.EffectiveWidth(calls.SequenceWidth));
				values["numbering.calls.year_start"] = YearStartValue(calls.YearStart());
				values["numbering.calls.next"] = ImportCanonical.Int((await _callNumbering.GetNextAsync(departmentId, calls, now))?.NextSequence ?? 1);

				var documents = await _settings.GetDocumentNumberingConfigAsync(departmentId, fresh) ?? new DocumentNumberingConfig();
				values["numbering.documents.year_start"] = YearStartValue(documents.YearStart());
				var statuses = await _documentNumbering.GetStatusesAsync(departmentId, false, now) ?? new List<DocumentNumberStatus>();
				foreach (var kind in ImportConfigurationCatalog.DocumentKinds)
				{
					var status = statuses.FirstOrDefault(s => s.Kind == kind);
					var custom = status?.Custom == true;
					values["numbering." + kind + ".pattern"] = custom ? status.Pattern : string.Empty;
					values["numbering." + kind + ".width"] = custom ? ImportCanonical.Int(status.SequenceWidth) : string.Empty;
					values["numbering." + kind + ".next"] = custom ? ImportCanonical.Int(status.NextSequence) : string.Empty;
				}
			}

			if (groups.Contains(ImportConfigurationGroups.Records) && await RecordsAvailableAsync(departmentId))
			{
				var records = await _settings.GetRecordsNumberingConfigAsync(departmentId, fresh) ?? new RecordsNumberingConfig();
				values["records.pattern"] = RecordNumberFormat.EffectivePattern(records);
				values["records.width"] = ImportCanonical.Int(RecordNumberFormat.EffectiveWidth(records.SequenceWidth));
				values["records.year_start"] = YearStartValue(records.YearStart());
				// Next numbers of the year a record started now falls in, for records with no station or group.
				var sequences = await RecordSequencesAsync(departmentId, records, now);
				foreach (var type in ImportConfigurationCatalog.NumberedRecordTypes)
				{
					var slug = ImportConfigurationCatalog.RecordTypeSlug(type);
					values["records." + slug + ".prefix"] = records.PrefixFor(type);
					values["records." + slug + ".next"] = ImportCanonical.Int(RecordSequence(sequences, type)?.NextSequence ?? 1);
				}

				var statuses = await _documentNumbering.GetStatusesAsync(departmentId, true, now) ?? new List<DocumentNumberStatus>();
				foreach (var kind in ImportConfigurationCatalog.RecordsDocumentKinds)
				{
					var status = statuses.FirstOrDefault(s => s.Kind == kind);
					var custom = status?.Custom == true;
					values["records." + kind + ".pattern"] = custom ? status.Pattern : string.Empty;
					values["records." + kind + ".width"] = custom ? ImportCanonical.Int(status.SequenceWidth) : string.Empty;
					values["records." + kind + ".next"] = custom ? ImportCanonical.Int(status.NextSequence) : string.Empty;
				}
			}

			if (groups.Contains(ImportConfigurationGroups.Access))
			{
				foreach (var name in ImportConfigurationCatalog.Permissions)
				{
					var row = await _permissions.GetPermissionByDepartmentTypeAsync(departmentId, Enum.Parse<PermissionTypes>(name));
					values["permission." + name] = PermissionValue(row);
				}
			}

			return values.Where(v => wanted.Any(i => i.Key == v.Key)).ToDictionary(v => v.Key, v => v.Value, StringComparer.Ordinal);
		}

		/// <summary>The canonical value of a permission row; no row is the Security screen's "Everyone" for every item.</summary>
		public static string PermissionValue(Permission row)
		{
			if (row == null)
				return ImportConfigurationCatalog.Permission((int)PermissionActions.Everyone, false, null);

			var roles = (row.Data ?? string.Empty).Split(',').Select(r => int.TryParse(r.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : 0);
			return ImportConfigurationCatalog.Permission(row.Action, row.LockToGroup, roles);
		}

		private static string TimeZoneValue(string zone)
		{
			if (string.IsNullOrWhiteSpace(zone))
				return string.Empty;
			return TimeZones.Zones.Keys.FirstOrDefault(z => string.Equals(z, zone.Trim(), StringComparison.OrdinalIgnoreCase)) ?? zone.Trim();
		}

		private static string AddressValue(Address address) => ImportConfigurationCatalog.AddressJson(new ImportConfigurationAddress
		{
			Address1 = address.Address1, City = address.City, State = address.State, PostalCode = address.PostalCode, Country = address.Country
		});

		/// <summary>The sequences of the numbering year a record started at <paramref name="utcNow"/> falls in (the department's time zone).</summary>
		private async Task<List<RecordNumberSequenceStatus>> RecordSequencesAsync(int departmentId, RecordsNumberingConfig config, DateTime utcNow)
		{
			var department = await _departments.GetDepartmentByIdAsync(departmentId, false);
			var year = RecordsNumberingService.NumberingYear(config, utcNow, department?.TimeZone);
			return await _recordsNumbering.GetSequencesAsync(departmentId, config, year) ?? new List<RecordNumberSequenceStatus>();
		}

		/// <summary>A record type's sequence for records with no station or group (the one a department-wide pattern has).</summary>
		private static RecordNumberSequenceStatus RecordSequence(IEnumerable<RecordNumberSequenceStatus> sequences, string definitionKey) =>
			sequences.FirstOrDefault(s => s.GroupId == null && s.DefinitionKeys.Contains(definitionKey, StringComparer.Ordinal));

		private static string YearStartValue(NumberingYearStart start) =>
			start == null ? ImportConfigurationCatalog.MonthDay(1, 1) : ImportConfigurationCatalog.MonthDay(start.Month, start.Day);

		public static bool ModuleDisabled(DepartmentModuleSettings modules, string module) =>
			typeof(DepartmentModuleSettings).GetProperty(module + "Disabled")?.GetValue(modules) as bool? == true;

		private static void SetModuleDisabled(DepartmentModuleSettings modules, string module, bool disabled) =>
			typeof(DepartmentModuleSettings).GetProperty(module + "Disabled")?.SetValue(modules, disabled);

		// ---- Write -----------------------------------------------------------------------------------------------------

		/// <summary>
		/// Writes one item's canonical value through its owning service on behalf of <paramref name="actingUserId"/> (the
		/// approving administrator). The caller journals the before-value it read first.
		/// </summary>
		public async Task<ImportConfigurationWriteResult> WriteAsync(int departmentId, string actingUserId, string workspaceId, ImportConfigurationItem item,
			string value, CancellationToken cancellationToken = default)
		{
			value ??= string.Empty;
			switch (item.Group)
			{
				case ImportConfigurationGroups.Department:
					return await WriteDepartmentAsync(departmentId, item, value, cancellationToken);

				case ImportConfigurationGroups.Branding:
					return await WriteBrandingAsync(departmentId, actingUserId, workspaceId, item, value, cancellationToken);

				case ImportConfigurationGroups.Modules:
				{
					var modules = await _settings.GetDepartmentModuleSettingsAsync(departmentId, true) ?? new DepartmentModuleSettings();
					SetModuleDisabled(modules, item.Target, value != "true");
					await _settings.SetDepartmentModuleSettingsAsync(departmentId, modules, cancellationToken);
					return ImportConfigurationWriteResult.Ok();
				}

				case ImportConfigurationGroups.Terminology:
				{
					var terminology = await _settings.GetDepartmentTerminologyAsync(departmentId, true) ?? new DepartmentTerminologyConfig();
					terminology.Set(item.Target, value);
					await _settings.SetDepartmentTerminologyAsync(departmentId, terminology, cancellationToken);
					return ImportConfigurationWriteResult.Ok();
				}

				case ImportConfigurationGroups.Map:
					return await WriteMapAsync(departmentId, item, value, cancellationToken);

				case ImportConfigurationGroups.Numbering:
					return await WriteNumberingAsync(departmentId, actingUserId, item, value, cancellationToken);

				case ImportConfigurationGroups.Records:
					return await WriteRecordsNumberingAsync(departmentId, actingUserId, item, value, cancellationToken);

				case ImportConfigurationGroups.Access:
				{
					var permission = ImportConfigurationCatalog.ReadJson<ImportConfigurationPermission>(value);
					if (permission == null)
						return ImportConfigurationWriteResult.Refused("IMP-CONFIG-INVALID");
					var data = permission.Roles?.Count > 0 ? string.Join(",", permission.Roles.Select(r => r.ToString(CultureInfo.InvariantCulture))) : null;
					await _permissions.SetPermissionForDepartmentAsync(departmentId, actingUserId, Enum.Parse<PermissionTypes>(item.Target),
						(PermissionActions)permission.Action, data, permission.LockToGroup, cancellationToken);
					return ImportConfigurationWriteResult.Ok();
				}

				default:
					return ImportConfigurationWriteResult.Refused("IMP-CONFIG-UNKNOWN");
			}
		}

		private async Task<ImportConfigurationWriteResult> WriteDepartmentAsync(int departmentId, ImportConfigurationItem item, string value, CancellationToken cancellationToken)
		{
			var department = await _departments.GetDepartmentByIdAsync(departmentId, true);
			if (department == null)
				return ImportConfigurationWriteResult.Refused("IMP-TARGET-MISSING");

			// The managing user is never changed by an import (Loops sync, billing ownership; section 8.1 A).
			switch (item.Key)
			{
				case "department.name":
					if (string.IsNullOrWhiteSpace(value))
						return ImportConfigurationWriteResult.Refused("IMP-CONFIG-REQUIRED");
					department.Name = value;
					break;
				case "department.time_zone":
					department.TimeZone = string.IsNullOrWhiteSpace(value) ? department.TimeZone : value;
					break;
				case "department.use_24_hour_time":
					department.Use24HourTime = value == "true";
					break;
				case "department.address":
				{
					if (string.IsNullOrWhiteSpace(value))
					{
						department.AddressId = null;
						break;
					}

					var proposed = ImportConfigurationCatalog.ReadJson<ImportConfigurationAddress>(value);
					if (proposed == null)
						return ImportConfigurationWriteResult.Refused("IMP-CONFIG-INVALID");
					// The department's own address row is edited in place, as the Address screen does; a department with none gets one.
					var address = department.AddressId is int addressId && addressId > 0 ? await _addresses.GetAddressByIdAsync(addressId) : null;
					address ??= new Address();
					address.Address1 = proposed.Address1;
					address.City = proposed.City;
					address.State = proposed.State;
					address.PostalCode = proposed.PostalCode;
					address.Country = proposed.Country;
					address = await _addresses.SaveAddressAsync(address, cancellationToken);
					department.AddressId = address.AddressId;
					break;
				}
				default:
					return ImportConfigurationWriteResult.Refused("IMP-CONFIG-UNKNOWN");
			}

			await _departments.UpdateDepartmentAsync(department, cancellationToken);
			await _departments.InvalidateAllDepartmentsCache(departmentId);
			return ImportConfigurationWriteResult.Ok();
		}

		private async Task<ImportConfigurationWriteResult> WriteBrandingAsync(int departmentId, string actingUserId, string workspaceId, ImportConfigurationItem item,
			string value, CancellationToken cancellationToken)
		{
			if (item.Key == "branding.logo")
			{
				if (string.IsNullOrWhiteSpace(value))
				{
					await _profiles.RemoveLogoAsync(departmentId, actingUserId, cancellationToken);
					return ImportConfigurationWriteResult.Ok();
				}

				var artifact = await _artifacts.GetByIdAsync(departmentId, value, cancellationToken);
				if (artifact == null || artifact.WorkspaceId != workspaceId || artifact.Kind != (int)ImportArtifactKind.Logo ||
					artifact.UploadState != (int)ImportArtifactUploadState.Completed || artifact.PurgedOn.HasValue)
					return ImportConfigurationWriteResult.Refused("IMP-CONFIG-LOGO-MISSING");

				byte[] data;
				try
				{
					data = await ReadLogoAsync(artifact, cancellationToken);
				}
				catch (Exception ex) when (!(ex is OperationCanceledException))
				{
					Logging.LogException(ex, $"Department import: logo {artifact.Id} could not be read");
					return ImportConfigurationWriteResult.Refused("IMP-CONFIG-LOGO-MISSING");
				}

				try
				{
					await _profiles.UploadLogoAsync(departmentId, actingUserId, artifact.OriginalName, artifact.DeclaredContentType, data, cancellationToken);
				}
				catch (DepartmentLogoRejectedException)
				{
					return ImportConfigurationWriteResult.Refused("IMP-CONFIG-LOGO-REJECTED");
				}

				return ImportConfigurationWriteResult.Ok();
			}

			var profile = await _profiles.GetOrCreateProfileAsync(departmentId, cancellationToken);
			var text = string.IsNullOrWhiteSpace(value) ? null : value;
			switch (item.Key)
			{
				case "branding.short_name": profile.ShortName = text; break;
				case "branding.description": profile.Description = text; break;
				case "branding.phone": profile.PhoneNumber = text; break;
				case "branding.website": profile.Website = text; break;
				default: return ImportConfigurationWriteResult.Refused("IMP-CONFIG-UNKNOWN");
			}

			await _profiles.SaveProfileAsync(profile, cancellationToken);
			return ImportConfigurationWriteResult.Ok();
		}

		/// <summary>The plaintext of an uploaded logo (sealed whole by <see cref="StoreLogoAsync"/>).</summary>
		public async Task<byte[]> ReadLogoAsync(ImportArtifact artifact, CancellationToken cancellationToken)
		{
			var key = await _keys.UnwrapAsync(artifact.DepartmentId, artifact.WrappedDataKey);
			var sealedBytes = await _store.ReadRangeAsync(artifact.ObjectKey, 0, (int)artifact.SizeBytes, cancellationToken);
			return _cipher.DecryptWhole(key, artifact.Id, sealedBytes);
		}

		/// <summary>Seals and stores an uploaded logo under the workspace's prefix; the caller has validated it.</summary>
		public async Task StoreLogoAsync(ImportArtifact artifact, byte[] data, CancellationToken cancellationToken)
		{
			var (wrapped, plain) = await _keys.CreateKeyAsync(artifact.DepartmentId);
			artifact.WrappedDataKey = wrapped;
			artifact.ObjectKey = ImportArtifactKeys.ForArtifact(artifact.DepartmentId, artifact.WorkspaceId, artifact.Id);
			var sealedBytes = _cipher.EncryptWhole(plain, artifact.Id, data);
			await _store.PutAsync(artifact.ObjectKey, sealedBytes, cancellationToken);
			artifact.SizeBytes = sealedBytes.Length;
		}

		private async Task<ImportConfigurationWriteResult> WriteMapAsync(int departmentId, ImportConfigurationItem item, string value, CancellationToken cancellationToken)
		{
			switch (item.Key)
			{
				case "map.center":
				{
					if (string.IsNullOrWhiteSpace(value))
					{
						await _settings.DeleteSettingAsync(departmentId, DepartmentSettingTypes.BigBoardMapCenterGpsCoordinates, cancellationToken);
						return ImportConfigurationWriteResult.Ok();
					}

					var parts = value.Split(',');
					// Both coordinates are given, so the service stores them without geocoding anything.
					var saved = await _settings.SaveMapCenterCoordinatesAsync(departmentId, parts[0], parts[1], null, cancellationToken);
					return saved == null ? ImportConfigurationWriteResult.Refused("IMP-CONFIG-INVALID") : ImportConfigurationWriteResult.Ok();
				}

				case "map.zoom":
					if (string.IsNullOrWhiteSpace(value))
						await _settings.DeleteSettingAsync(departmentId, DepartmentSettingTypes.BigBoardMapZoomLevel, cancellationToken);
					else
						await _settings.SaveOrUpdateSettingAsync(departmentId, value, DepartmentSettingTypes.BigBoardMapZoomLevel, cancellationToken);
					return ImportConfigurationWriteResult.Ok();

				case "map.day_style":
				case "map.night_style":
				{
					var day = await _settings.GetMappingMapStyleAsync(departmentId, true);
					var night = await _settings.GetMappingMapStyleNightAsync(departmentId, true);
					var style = Enum.TryParse<MapStyleTypes>(string.IsNullOrWhiteSpace(value) ? ImportConfigurationCatalog.MapAutomatic : value, true, out var parsed)
						? parsed
						: MapStyleTypes.Automatic;
					if (item.Key == "map.day_style")
						day = style;
					else
						night = style;
					await _settings.SaveMappingMapStylesAsync(departmentId, day, night, cancellationToken);
					return ImportConfigurationWriteResult.Ok();
				}

				default:
					return ImportConfigurationWriteResult.Refused("IMP-CONFIG-UNKNOWN");
			}
		}

		private async Task<ImportConfigurationWriteResult> WriteNumberingAsync(int departmentId, string actingUserId, ImportConfigurationItem item, string value,
			CancellationToken cancellationToken)
		{
			var field = item.Key.Substring(item.Key.LastIndexOf('.') + 1);
			var number = ImportCanonical.ToNullableInt(value);

			if (item.Target == "calls")
			{
				var config = await _settings.GetCallNumberingConfigAsync(departmentId, true) ?? new CallNumberingConfig();
				var start = config.YearStart();
				var update = new CallNumberingUpdate
				{
					Pattern = CallNumberFormat.EffectivePattern(config),
					SequenceWidth = CallNumberFormat.EffectiveWidth(config.SequenceWidth),
					YearStartMonth = start.Month,
					YearStartDay = start.Day,
					YearLabel = (int)start.Label
				};

				switch (field)
				{
					case "pattern":
						update.Pattern = value;
						break;
					case "width":
						update.SequenceWidth = number ?? update.SequenceWidth;
						break;
					case "year_start":
					{
						var parts = value.Split('-');
						update.YearStartMonth = ImportCanonical.ToInt(parts[0]);
						update.YearStartDay = ImportCanonical.ToInt(parts.Length > 1 ? parts[1] : null);
						break;
					}
					case "next":
					{
						if (number == null)
							return ImportConfigurationWriteResult.Refused("IMP-CONFIG-INVALID");
						var current = await _callNumbering.GetNextAsync(departmentId, config, DateTime.UtcNow);
						update.ScopeKey = current?.ScopeKey;
						update.NextSequence = number;
						break;
					}
				}

				var result = await _callNumbering.SaveAsync(departmentId, actingUserId, update, cancellationToken);
				if (result.PatternRejected)
					return ImportConfigurationWriteResult.Refused("IMP-CONFIG-PATTERN");
				if (result.YearStartRejected)
					return ImportConfigurationWriteResult.Refused("IMP-CONFIG-INVALID");
				if (result.BelowCurrent != null)
					return ImportConfigurationWriteResult.Refused("IMP-CONFIG-BELOW-CURRENT");
				if (result.NextNotApplied)
					return ImportConfigurationWriteResult.Refused("IMP-CONFIG-NOT-APPLIED");
				return ImportConfigurationWriteResult.Ok();
			}

			var documents = await _settings.GetDocumentNumberingConfigAsync(departmentId, true) ?? new DocumentNumberingConfig();
			var documentUpdate = new DocumentNumberingUpdate();
			if (item.Target == "documents")
			{
				var parts = value.Split('-');
				documentUpdate.YearStartMonth = ImportCanonical.ToInt(parts[0]);
				documentUpdate.YearStartDay = ImportCanonical.ToInt(parts.Length > 1 ? parts[1] : null);
			}
			else
			{
				var status = (await _documentNumbering.GetStatusesAsync(departmentId, false, DateTime.UtcNow) ?? new List<DocumentNumberStatus>())
					.FirstOrDefault(s => s.Kind == item.Target);
				var saved = DocumentNumbering.EffectivePattern(documents.Patterns, item.Target);
				var pattern = new DocumentNumberPatternUpdate
				{
					Kind = item.Target,
					Pattern = saved?.Pattern,
					SequenceWidth = saved?.SequenceWidth ?? 1
				};

				switch (field)
				{
					case "pattern":
						pattern.Pattern = value;
						break;
					case "width":
						if (saved == null)
							return ImportConfigurationWriteResult.Refused("IMP-CONFIG-NEEDS-PATTERN");
						pattern.SequenceWidth = number ?? pattern.SequenceWidth;
						break;
					case "next":
						if (saved == null || status?.Custom != true)
							return ImportConfigurationWriteResult.Refused("IMP-CONFIG-NEEDS-PATTERN");
						pattern.ScopeKey = status.ScopeKey;
						pattern.NextSequence = number;
						break;
				}

				documentUpdate.Patterns.Add(pattern);
			}

			var saveResult = await _documentNumbering.SaveAsync(departmentId, actingUserId, documentUpdate, cancellationToken);
			if (saveResult.YearStartRejected)
				return ImportConfigurationWriteResult.Refused("IMP-CONFIG-INVALID");
			if (saveResult.PatternsRejected?.Count > 0)
				return ImportConfigurationWriteResult.Refused("IMP-CONFIG-PATTERN");
			if (saveResult.BelowCurrent?.Count > 0)
				return ImportConfigurationWriteResult.Refused("IMP-CONFIG-BELOW-CURRENT");
			if (saveResult.NotApplied > 0)
				return ImportConfigurationWriteResult.Refused("IMP-CONFIG-NOT-APPLIED");
			return ImportConfigurationWriteResult.Ok();
		}

		/// <summary>
		/// Writes one part of record numbering through the Records Settings screen's own services: setting 72's pattern, width,
		/// year start and type prefixes, a type's next number (checked against the saved pattern, so it may only rise), or the
		/// pattern, width and next number of a records request, prevention, investigation or evidence number.
		/// </summary>
		private async Task<ImportConfigurationWriteResult> WriteRecordsNumberingAsync(int departmentId, string actingUserId, ImportConfigurationItem item, string value,
			CancellationToken cancellationToken)
		{
			if (!await RecordsAvailableAsync(departmentId))
				return ImportConfigurationWriteResult.Refused("IMP-CONFIG-UNAVAILABLE");

			var field = item.Key.Substring(item.Key.LastIndexOf('.') + 1);
			var number = ImportCanonical.ToNullableInt(value);

			if (ImportConfigurationCatalog.RecordsDocumentKinds.Contains(item.Target))
			{
				var status = (await _documentNumbering.GetStatusesAsync(departmentId, true, DateTime.UtcNow) ?? new List<DocumentNumberStatus>())
					.FirstOrDefault(s => s.Kind == item.Target);
				var saved = status?.Custom == true ? status : null;
				var update = new DocumentNumberPatternUpdate { Kind = item.Target, Pattern = saved?.Pattern, SequenceWidth = saved?.SequenceWidth ?? 1 };
				switch (field)
				{
					case "pattern":
						update.Pattern = value;
						break;
					case "width":
						if (saved == null)
							return ImportConfigurationWriteResult.Refused("IMP-CONFIG-NEEDS-PATTERN");
						update.SequenceWidth = number ?? update.SequenceWidth;
						break;
					case "next":
						if (saved == null)
							return ImportConfigurationWriteResult.Refused("IMP-CONFIG-NEEDS-PATTERN");
						update.ScopeKey = saved.ScopeKey;
						update.NextSequence = number;
						break;
				}

				var saveResult = await _documentNumbering.SaveRecordsPatternsAsync(departmentId, actingUserId, new List<DocumentNumberPatternUpdate> { update }, cancellationToken);
				if (saveResult.PatternsRejected?.Count > 0)
					return ImportConfigurationWriteResult.Refused("IMP-CONFIG-PATTERN");
				if (saveResult.BelowCurrent?.Count > 0)
					return ImportConfigurationWriteResult.Refused("IMP-CONFIG-BELOW-CURRENT");
				if (saveResult.NotApplied > 0)
					return ImportConfigurationWriteResult.Refused("IMP-CONFIG-NOT-APPLIED");
				return ImportConfigurationWriteResult.Ok();
			}

			var config = await _settings.GetRecordsNumberingConfigAsync(departmentId, true) ?? new RecordsNumberingConfig();
			var numbering = new RecordsNumberingUpdate
			{
				Pattern = RecordNumberFormat.EffectivePattern(config),
				SequenceWidth = RecordNumberFormat.EffectiveWidth(config.SequenceWidth)
			};

			switch (field)
			{
				case "pattern":
					numbering.Pattern = value;
					break;
				case "width":
					numbering.SequenceWidth = number ?? numbering.SequenceWidth;
					break;
				case "year_start":
				{
					var parts = value.Split('-');
					numbering.YearStartMonth = ImportCanonical.ToInt(parts[0]);
					numbering.YearStartDay = ImportCanonical.ToInt(parts.Length > 1 ? parts[1] : null);
					break;
				}
				case "prefix":
					numbering.Prefixes = new List<RecordNumberPrefixRequest> { new RecordNumberPrefixRequest { DefinitionKey = item.Target, Prefix = value } };
					break;
				case "next":
				{
					// A pattern with {GROUP} numbers each station separately: those sequences are raised on the Records Settings screen.
					if (number == null)
						return ImportConfigurationWriteResult.Refused("IMP-CONFIG-INVALID");
					if (RecordNumberFormat.UsesToken(numbering.Pattern, RecordNumberFormat.GroupToken))
						return ImportConfigurationWriteResult.Refused("IMP-CONFIG-RECORDS-PER-GROUP");
					var now = DateTime.UtcNow;
					var department = await _departments.GetDepartmentByIdAsync(departmentId, true);
					numbering.Year = RecordsNumberingService.NumberingYear(config, now, department?.TimeZone);
					var sequence = RecordSequence(await _recordsNumbering.GetSequencesAsync(departmentId, config, numbering.Year) ?? new List<RecordNumberSequenceStatus>(), item.Target);
					if (sequence == null)
						return ImportConfigurationWriteResult.Refused("IMP-CONFIG-NOT-APPLIED");
					numbering.NextNumbers.Add(new RecordNextNumberRequest { ScopeKey = sequence.ScopeKey, NextSequence = number.Value });
					break;
				}
			}

			var result = await _recordsNumbering.SaveAsync(departmentId, actingUserId, numbering, cancellationToken);
			if (result.PatternRejected)
				return ImportConfigurationWriteResult.Refused("IMP-CONFIG-PATTERN");
			if (result.YearStartRejected)
				return ImportConfigurationWriteResult.Refused("IMP-CONFIG-INVALID");
			if (result.PrefixesRejected?.Count > 0)
				return ImportConfigurationWriteResult.Refused("IMP-CONFIG-PREFIX");
			if (result.BelowCurrent?.Count > 0)
				return ImportConfigurationWriteResult.Refused("IMP-CONFIG-BELOW-CURRENT");
			if (result.NotApplied > 0)
				return ImportConfigurationWriteResult.Refused("IMP-CONFIG-NOT-APPLIED");
			return ImportConfigurationWriteResult.Ok();
		}

		// ---- Rollback --------------------------------------------------------------------------------------------------

		/// <summary>
		/// Puts an item back to <paramref name="before"/> when it still reads <paramref name="after"/> (section 7.5 step 4).
		/// Next numbers only ever go up and a replaced logo is not kept, so those become manual reconciliation items.
		/// </summary>
		public async Task<(ImportRollbackOutcome Outcome, string Code)> RestoreAsync(int departmentId, string actingUserId, string workspaceId,
			ImportConfigurationItem item, string before, string after, CancellationToken cancellationToken = default)
		{
			before ??= string.Empty;
			after ??= string.Empty;
			var current = (await ReadAsync(departmentId, new[] { item }, true, cancellationToken)).TryGetValue(item.Key, out var value) ? value : string.Empty;

			if (item.Kind == ImportConfigurationValueKind.Logo)
			{
				if (current.Length == 0)
					return (ImportRollbackOutcome.None, null);
				if (before.Length > 0)
					return (ImportRollbackOutcome.ManualReconciliation, "IMP-ROLLBACK-LOGO-REPLACED");
				await _profiles.RemoveLogoAsync(departmentId, actingUserId, cancellationToken);
				return (ImportRollbackOutcome.Restored, null);
			}

			// Still the value from before (the write was refused, or someone already put it back): nothing to undo.
			if (string.Equals(current, before, StringComparison.Ordinal))
				return (ImportRollbackOutcome.None, null);
			if (item.Key.EndsWith(".next", StringComparison.Ordinal))
				return (ImportRollbackOutcome.ManualReconciliation, "IMP-ROLLBACK-NUMBER-KEPT");
			if (!string.Equals(current, after, StringComparison.Ordinal))
				return (ImportRollbackOutcome.ManualReconciliation, "IMP-ROLLBACK-CHANGED-SINCE");

			var written = await WriteAsync(departmentId, actingUserId, workspaceId, item, before, cancellationToken);
			return written.Written ? (ImportRollbackOutcome.Restored, null) : (ImportRollbackOutcome.ManualReconciliation, written.Code ?? "IMP-ROLLBACK-FAILED");
		}
	}
}
