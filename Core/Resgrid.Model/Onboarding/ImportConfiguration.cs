using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;

namespace Resgrid.Model.Onboarding
{
	/// <summary>Where a proposed configuration value came from (wizard plan section 6.3, step 9 "labels every value with its origin").</summary>
	public enum ImportConfigurationOrigin
	{
		/// <summary>Composed from the department's operating profile (section 7.2).</summary>
		Preset = 1,

		/// <summary>Worked out from the data being imported (a module the data needs, the middle of the imported stations).</summary>
		ImportedData = 2,

		/// <summary>Typed or chosen by the administrator.</summary>
		Administrator = 3,

		/// <summary>Entered by Resgrid staff under a Prepare or Operate grant (managed migration).</summary>
		Staff = 4,

		/// <summary>The word a product the department is moving from uses for the feature (its pack's glossary, section 7.4).</summary>
		Product = 5
	}

	/// <summary>
	/// The administrator's decision on one configuration item. Suggestions are computed on every read and are never stored;
	/// only decisions are.
	/// </summary>
	public enum ImportConfigurationChangeState
	{
		/// <summary>Will be written by the next approved import run.</summary>
		Accepted = 1,

		/// <summary>The suggestion was declined; it stops showing as waiting for a decision.</summary>
		Rejected = 2,

		/// <summary>Written by the run in <see cref="ImportConfigurationChange.AppliedRunId"/>.</summary>
		Applied = 3,

		/// <summary>The run could not write it (<see cref="ImportConfigurationChange.FailureCode"/>); it can be edited and approved again.</summary>
		Failed = 4,

		/// <summary>Written, then undone by a rollback.</summary>
		RolledBack = 5
	}

	/// <summary>How a configuration item's value is entered and validated.</summary>
	public enum ImportConfigurationValueKind
	{
		Text = 1,
		Boolean = 2,
		/// <summary>A Windows time zone id from <see cref="TimeZones.Zones"/>.</summary>
		TimeZone = 3,
		Choice = 4,
		Integer = 5,
		/// <summary>"latitude,longitude".</summary>
		Coordinates = 6,
		/// <summary>JSON <see cref="ImportConfigurationAddress"/>.</summary>
		Address = 7,
		/// <summary>A numbering pattern in the call number grammar.</summary>
		NumberPattern = 8,
		/// <summary>"MM-DD": the first day of the numbering year.</summary>
		MonthDay = 9,
		/// <summary>JSON <see cref="ImportConfigurationPermission"/>.</summary>
		Permission = 10,
		/// <summary>The id of a logo uploaded to the workspace (an <see cref="ImportArtifact"/> of kind Logo).</summary>
		Logo = 11,
		Url = 12,
		/// <summary>A record type's {PREFIX}: 2 to 6 upper-case letters or digits; empty is the type's shipped prefix.</summary>
		NumberPrefix = 13
	}

	/// <summary>
	/// A decided configuration value of a workspace (wizard plan sections 6.3 step 9 and 7.5): one row per item. The
	/// approval digest covers the accepted rows, the commit run writes them before any data set through the owning typed
	/// setters and journals the before-value, and rollback restores it while nobody has changed the setting since.
	/// </summary>
	public sealed class ImportConfigurationChange : ImportRow
	{
		public string WorkspaceId { get; set; }
		/// <summary><see cref="ImportConfigurationItem.Key"/>.</summary>
		public string ItemKey { get; set; }
		/// <summary><see cref="ImportConfigurationOrigin"/>.</summary>
		public int Origin { get; set; }
		/// <summary>What proposed it: the preset fragment ("pack.fire"), the data set entity, or null.</summary>
		public string OriginKey { get; set; }
		/// <summary>The canonical value (see <see cref="ImportConfigurationCatalog.Canonicalize"/>); empty = clear (back to Resgrid's default).</summary>
		public string Value { get; set; }
		/// <summary><see cref="ImportConfigurationChangeState"/>.</summary>
		public int State { get; set; }
		/// <summary>The commit run that will write it (set at approval, like a data set's ApprovedRunId).</summary>
		public string ApprovedRunId { get; set; }
		public string AppliedRunId { get; set; }
		public string FailureCode { get; set; }
		public bool StaffAuthored { get; set; }
		public int Revision { get; set; } = 1;
		public string CreatedBy { get; set; }
		public string UpdatedBy { get; set; }
		public DateTime CreatedOn { get; set; }
		public DateTime UpdatedOn { get; set; }
		public DateTime? AppliedOn { get; set; }
	}

	/// <summary>A department address as a configuration value (the MVC Address rules: street, city, region and country required).</summary>
	public sealed class ImportConfigurationAddress
	{
		public string Address1 { get; set; }
		public string City { get; set; }
		public string State { get; set; }
		public string PostalCode { get; set; }
		public string Country { get; set; }
	}

	/// <summary>A permission as a configuration value: the action, the group lock and the roles of a "select roles" action.</summary>
	public sealed class ImportConfigurationPermission
	{
		/// <summary><see cref="PermissionActions"/>.</summary>
		public int Action { get; set; }
		public bool LockToGroup { get; set; }
		public List<int> Roles { get; set; } = new List<int>();
	}

	/// <summary>One configurable item of the Configure step.</summary>
	public sealed class ImportConfigurationItem
	{
		public ImportConfigurationItem(string key, string group, ImportConfigurationValueKind kind, int order, string adminAssistId = null,
			int maxLength = 0, int min = 0, int max = 0, bool allowsBlank = false, IReadOnlyList<string> choices = null, string target = null)
		{
			Key = key;
			Group = group;
			Kind = kind;
			Order = order;
			AdminAssistId = adminAssistId;
			MaxLength = maxLength;
			Min = min;
			Max = max;
			AllowsBlank = allowsBlank;
			Choices = choices ?? Array.Empty<string>();
			Target = target;
		}

		/// <summary>Stable key ("terminology.module.units", "modules.Shifts", "numbering.calls.next"). Never rename one.</summary>
		public string Key { get; }
		/// <summary><see cref="ImportConfigurationGroups"/>.</summary>
		public string Group { get; }
		public ImportConfigurationValueKind Kind { get; }
		/// <summary>Apply and display order (the commit writes lower orders first).</summary>
		public int Order { get; }
		/// <summary>The Admin Assist catalog id of the setting this item writes, when it has one.</summary>
		public string AdminAssistId { get; }
		public int MaxLength { get; }
		public int Min { get; }
		public int Max { get; }
		/// <summary>An empty value is allowed and means "back to Resgrid's default" (a term's own name, built-in numbers).</summary>
		public bool AllowsBlank { get; }
		public IReadOnlyList<string> Choices { get; }
		/// <summary>The item's subject: the term key, module name, document kind or permission type.</summary>
		public string Target { get; }
	}

	/// <summary>The outcome groups of the Configure step, in the order the plan's step 9 lists them.</summary>
	public static class ImportConfigurationGroups
	{
		public const string Department = "department";
		public const string Branding = "branding";
		public const string Modules = "modules";
		public const string Terminology = "terminology";
		public const string Map = "map";
		public const string Numbering = "numbering";
		/// <summary>Record numbering (setting 72), shown only while Records is on for the department.</summary>
		public const string Records = "records";
		public const string Access = "access";

		public static readonly IReadOnlyList<string> All = new[] { Department, Branding, Modules, Terminology, Map, Numbering, Records, Access };
	}

	/// <summary>
	/// The Phase 2 configuration domains (wizard plan section 8.1 A): the department record, profile and logo, module
	/// switches and terminology, map, call and document numbering, and the permissions Admin Assist can preview. A
	/// permission is never read from a file (section 8.1): it is a preset or the administrator's choice. The operating
	/// profile is not an item: step 2 saves it directly through its own revision-checked editor.
	/// </summary>
	public static class ImportConfigurationCatalog
	{
		/// <summary>The journal entity type of configuration entries (rollback routes them here, not to a target writer).</summary>
		public const string JournalEntityType = "configuration";

		public const string MapAutomatic = "Automatic";

		/// <summary>The module switches an import may turn on or off (AI is configured on its own screen, section 8.1 D).</summary>
		public static readonly IReadOnlyList<string> Modules = new[]
		{
			"Messaging", "Mapping", "Shifts", "Logs", "Reports", "Documents", "Calendar", "Notes", "Training", "Inventory", "Maintenance",
			"Checklists", "BusinessOperations"
		};

		/// <summary>The document kinds numbered outside Records (Records numbering is configured with Records).</summary>
		public static readonly IReadOnlyList<string> DocumentKinds = new[]
		{
			DocumentNumberKinds.WorkOrder, DocumentNumberKinds.Invoice, DocumentNumberKinds.Bid, DocumentNumberKinds.TimeReport
		};

		/// <summary>The other numbers Records issues, configured with Records numbering and numbered by its year start.</summary>
		public static readonly IReadOnlyList<string> RecordsDocumentKinds = new[]
		{
			DocumentNumberKinds.RecordsRequest, DocumentNumberKinds.Occupancy, DocumentNumberKinds.Inspection, DocumentNumberKinds.Permit,
			DocumentNumberKinds.Investigation, DocumentNumberKinds.Evidence
		};

		/// <summary>The system record types the department pattern numbers (each with its own {PREFIX}); department definitions number themselves.</summary>
		public static readonly IReadOnlyList<string> NumberedRecordTypes =
			RmsDefinitionKeys.LockedTypes.Keys.Concat(new[] { RmsDefinitionKeys.NerisIncidentReport }).ToArray();

		/// <summary>A numbered record type's part of an item key: "system.unit-activity" is "unit-activity".</summary>
		public static string RecordTypeSlug(string definitionKey) =>
			definitionKey.StartsWith(RmsDefinitionKeys.SystemPrefix, StringComparison.Ordinal) ? definitionKey.Substring(RmsDefinitionKeys.SystemPrefix.Length) : definitionKey;

		/// <summary>The permissions Admin Assist's permission impact preview covers; an import never sets any other.</summary>
		public static readonly IReadOnlyList<string> Permissions = new[]
		{
			nameof(PermissionTypes.CreateCall), nameof(PermissionTypes.CreateNote), nameof(PermissionTypes.ViewPersonalInfo),
			nameof(PermissionTypes.ViewGroupUsers), nameof(PermissionTypes.ViewGroupUnits), nameof(PermissionTypes.CanSeePersonnelLocations),
			nameof(PermissionTypes.CanSeeUnitLocations)
		};

		/// <summary>Permissions whose group lock means something (the Security screen offers it).</summary>
		public static readonly IReadOnlyList<string> GroupScopedPermissions = new[]
		{
			nameof(PermissionTypes.ViewGroupUsers), nameof(PermissionTypes.ViewGroupUnits), nameof(PermissionTypes.CanSeePersonnelLocations),
			nameof(PermissionTypes.CanSeeUnitLocations)
		};

		/// <summary>The actions the Security screen offers these permissions, in its order (Everyone first).</summary>
		public static readonly IReadOnlyList<int> PermissionActionChoices = new[]
		{
			(int)PermissionActions.Everyone, (int)PermissionActions.DepartmentAdminsOnly, (int)PermissionActions.DepartmentAndGroupAdmins,
			(int)PermissionActions.DepartmentAdminsAndSelectRoles
		};

		private static readonly IReadOnlyList<string> MapStyles =
			new[] { MapAutomatic }.Concat(MapStylePresets.SelectableStyles.Select(s => s.ToString())).ToArray();

		public static readonly IReadOnlyList<ImportConfigurationItem> All = Build();

		private static readonly Dictionary<string, ImportConfigurationItem> ByKey = All.ToDictionary(i => i.Key, StringComparer.Ordinal);

		private static IReadOnlyList<ImportConfigurationItem> Build()
		{
			var items = new List<ImportConfigurationItem>
			{
				new ImportConfigurationItem("department.name", ImportConfigurationGroups.Department, ImportConfigurationValueKind.Text, 100, maxLength: 150),
				new ImportConfigurationItem("department.time_zone", ImportConfigurationGroups.Department, ImportConfigurationValueKind.TimeZone, 110),
				new ImportConfigurationItem("department.use_24_hour_time", ImportConfigurationGroups.Department, ImportConfigurationValueKind.Boolean, 120),
				new ImportConfigurationItem("department.address", ImportConfigurationGroups.Department, ImportConfigurationValueKind.Address, 130),

				new ImportConfigurationItem("branding.short_name", ImportConfigurationGroups.Branding, ImportConfigurationValueKind.Text, 200, maxLength: 50, allowsBlank: true),
				new ImportConfigurationItem("branding.description", ImportConfigurationGroups.Branding, ImportConfigurationValueKind.Text, 210, maxLength: 2000, allowsBlank: true),
				new ImportConfigurationItem("branding.phone", ImportConfigurationGroups.Branding, ImportConfigurationValueKind.Text, 220, maxLength: 50, allowsBlank: true),
				new ImportConfigurationItem("branding.website", ImportConfigurationGroups.Branding, ImportConfigurationValueKind.Url, 230, maxLength: 500, allowsBlank: true),
				new ImportConfigurationItem("branding.logo", ImportConfigurationGroups.Branding, ImportConfigurationValueKind.Logo, 240)
			};

			var order = 300;
			foreach (var module in Modules)
				items.Add(new ImportConfigurationItem("modules." + module, ImportConfigurationGroups.Modules, ImportConfigurationValueKind.Boolean, order++,
					adminAssistId: "module." + module + "Disabled", target: module));

			order = 400;
			foreach (var term in DepartmentTerms.All)
				items.Add(new ImportConfigurationItem("terminology." + term.Key, ImportConfigurationGroups.Terminology, ImportConfigurationValueKind.Text, order++,
					adminAssistId: "setting.DepartmentTerminology", maxLength: DepartmentTerms.MaxLength, allowsBlank: true, target: term.Key));

			items.Add(new ImportConfigurationItem("map.center", ImportConfigurationGroups.Map, ImportConfigurationValueKind.Coordinates, 500,
				adminAssistId: "setting.BigBoardMapCenterGpsCoordinates"));
			items.Add(new ImportConfigurationItem("map.zoom", ImportConfigurationGroups.Map, ImportConfigurationValueKind.Integer, 510,
				adminAssistId: "setting.BigBoardMapZoomLevel", min: 0, max: 15));
			items.Add(new ImportConfigurationItem("map.day_style", ImportConfigurationGroups.Map, ImportConfigurationValueKind.Choice, 520,
				adminAssistId: "setting.MappingMapStyle", choices: MapStyles));
			items.Add(new ImportConfigurationItem("map.night_style", ImportConfigurationGroups.Map, ImportConfigurationValueKind.Choice, 530,
				adminAssistId: "setting.MappingMapStyleNight", choices: MapStyles));

			// Numbering: a pattern and year start are written before the next number, which is checked against them.
			items.Add(new ImportConfigurationItem("numbering.calls.pattern", ImportConfigurationGroups.Numbering, ImportConfigurationValueKind.NumberPattern, 600,
				adminAssistId: "setting.CallNumberingConfig", maxLength: CallNumberFormat.MaxPatternLength, target: "calls"));
			items.Add(new ImportConfigurationItem("numbering.calls.width", ImportConfigurationGroups.Numbering, ImportConfigurationValueKind.Integer, 601,
				adminAssistId: "setting.CallNumberingConfig", min: 1, max: 8, target: "calls"));
			items.Add(new ImportConfigurationItem("numbering.calls.year_start", ImportConfigurationGroups.Numbering, ImportConfigurationValueKind.MonthDay, 602,
				adminAssistId: "setting.CallNumberingConfig", target: "calls"));
			items.Add(new ImportConfigurationItem("numbering.calls.next", ImportConfigurationGroups.Numbering, ImportConfigurationValueKind.Integer, 603,
				adminAssistId: "setting.CallNumberingConfig", min: 1, max: CallNumberFormat.MaxSequence, target: "calls"));
			items.Add(new ImportConfigurationItem("numbering.documents.year_start", ImportConfigurationGroups.Numbering, ImportConfigurationValueKind.MonthDay, 610,
				adminAssistId: "setting.DocumentNumberingConfig", target: "documents"));

			order = 620;
			foreach (var kind in DocumentKinds)
			{
				var max = DocumentNumberKinds.Find(kind).MaxLength;
				items.Add(new ImportConfigurationItem("numbering." + kind + ".pattern", ImportConfigurationGroups.Numbering, ImportConfigurationValueKind.NumberPattern, order++,
					adminAssistId: "setting.DocumentNumberingConfig", maxLength: Math.Min(max, CallNumberFormat.MaxPatternLength), allowsBlank: true, target: kind));
				items.Add(new ImportConfigurationItem("numbering." + kind + ".width", ImportConfigurationGroups.Numbering, ImportConfigurationValueKind.Integer, order++,
					adminAssistId: "setting.DocumentNumberingConfig", min: 1, max: 8, target: kind));
				items.Add(new ImportConfigurationItem("numbering." + kind + ".next", ImportConfigurationGroups.Numbering, ImportConfigurationValueKind.Integer, order++,
					adminAssistId: "setting.DocumentNumberingConfig", min: 1, max: CallNumberFormat.MaxSequence, target: kind));
			}

			// Record numbering (setting 72): the pattern, width and year start first, then each type's prefix (a new prefix is a new
			// sequence), then each type's next number, checked against what was just written; then the other Records numbers.
			items.Add(new ImportConfigurationItem("records.pattern", ImportConfigurationGroups.Records, ImportConfigurationValueKind.NumberPattern, 640,
				adminAssistId: "setting.RecordsNumberingConfig", maxLength: RecordNumberFormat.MaxPatternLength, target: "records"));
			items.Add(new ImportConfigurationItem("records.width", ImportConfigurationGroups.Records, ImportConfigurationValueKind.Integer, 641,
				adminAssistId: "setting.RecordsNumberingConfig", min: RecordNumberFormat.MinWidth, max: RecordNumberFormat.MaxWidth, target: "records"));
			items.Add(new ImportConfigurationItem("records.year_start", ImportConfigurationGroups.Records, ImportConfigurationValueKind.MonthDay, 642,
				adminAssistId: "setting.RecordsNumberingConfig", target: "records"));
			order = 643;
			foreach (var type in NumberedRecordTypes)
				items.Add(new ImportConfigurationItem("records." + RecordTypeSlug(type) + ".prefix", ImportConfigurationGroups.Records, ImportConfigurationValueKind.NumberPrefix,
					order++, adminAssistId: "setting.RecordsNumberingConfig", maxLength: RecordNumberFormat.MaxPrefixLength, target: type));
			foreach (var type in NumberedRecordTypes)
				items.Add(new ImportConfigurationItem("records." + RecordTypeSlug(type) + ".next", ImportConfigurationGroups.Records, ImportConfigurationValueKind.Integer,
					order++, adminAssistId: "setting.RecordsNumberingConfig", min: 1, max: RecordNumberFormat.MaxSequence, target: type));
			order = 660;
			foreach (var kind in RecordsDocumentKinds)
			{
				var max = DocumentNumberKinds.Find(kind).MaxLength;
				items.Add(new ImportConfigurationItem("records." + kind + ".pattern", ImportConfigurationGroups.Records, ImportConfigurationValueKind.NumberPattern, order++,
					adminAssistId: "setting.RecordsNumberingConfig", maxLength: Math.Min(max, CallNumberFormat.MaxPatternLength), allowsBlank: true, target: kind));
				items.Add(new ImportConfigurationItem("records." + kind + ".width", ImportConfigurationGroups.Records, ImportConfigurationValueKind.Integer, order++,
					adminAssistId: "setting.RecordsNumberingConfig", min: 1, max: 8, target: kind));
				items.Add(new ImportConfigurationItem("records." + kind + ".next", ImportConfigurationGroups.Records, ImportConfigurationValueKind.Integer, order++,
					adminAssistId: "setting.RecordsNumberingConfig", min: 1, max: CallNumberFormat.MaxSequence, target: kind));
			}

			order = 700;
			foreach (var permission in Permissions)
				items.Add(new ImportConfigurationItem("permission." + permission, ImportConfigurationGroups.Access, ImportConfigurationValueKind.Permission, order++,
					adminAssistId: "permission." + permission, target: permission));

			return items.OrderBy(i => i.Order).ToList();
		}

		public static ImportConfigurationItem Find(string key) =>
			key != null && ByKey.TryGetValue(key, out var item) ? item : null;

		/// <summary>
		/// The canonical form of a value for <paramref name="item"/>, or null with an issue code when it is not acceptable.
		/// Canonical forms are what is stored, compared, digested and journaled; equal values have equal canonical forms.
		/// </summary>
		public static (string Value, string Code) Canonicalize(ImportConfigurationItem item, string raw)
		{
			if (item == null)
				return (null, "IMP-CONFIG-UNKNOWN");

			var text = raw?.Trim() ?? string.Empty;
			// A record type with no prefix of its own writes the shipped one, so an empty prefix reads back as that.
			if (item.Kind == ImportConfigurationValueKind.NumberPrefix)
			{
				var prefix = text.Length == 0 ? RmsDefinitionKeys.DefaultNumberPrefix(item.Target) : text.ToUpperInvariant();
				return RecordNumberFormat.IsValidPrefix(prefix) ? (prefix, null) : (null, "IMP-CONFIG-PREFIX");
			}
			if (text.Length == 0 && item.Kind != ImportConfigurationValueKind.Address && item.Kind != ImportConfigurationValueKind.Permission)
				return item.AllowsBlank ? (string.Empty, null) : (null, "IMP-CONFIG-REQUIRED");

			switch (item.Kind)
			{
				case ImportConfigurationValueKind.Text:
				{
					var normalized = item.Group == ImportConfigurationGroups.Terminology ? DepartmentTerms.Normalize(text) ?? string.Empty : text;
					if (normalized.Length == 0)
						return item.AllowsBlank ? (string.Empty, null) : (null, "IMP-CONFIG-REQUIRED");
					return item.MaxLength > 0 && normalized.Length > item.MaxLength ? (null, "IMP-CONFIG-TOO-LONG") : (normalized, null);
				}

				case ImportConfigurationValueKind.Url:
					if (item.MaxLength > 0 && text.Length > item.MaxLength)
						return (null, "IMP-CONFIG-TOO-LONG");
					return Uri.TryCreate(text, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
						? (text, null)
						: (null, "IMP-CONFIG-INVALID");

				case ImportConfigurationValueKind.Boolean:
					switch (text.ToLowerInvariant())
					{
						case "true": case "yes": case "on": case "1": return ("true", null);
						case "false": case "no": case "off": case "0": return ("false", null);
						default: return (null, "IMP-CONFIG-INVALID");
					}

				case ImportConfigurationValueKind.TimeZone:
				{
					var zone = TimeZones.Zones.Keys.FirstOrDefault(z => string.Equals(z, text, StringComparison.OrdinalIgnoreCase));
					return zone != null ? (zone, null) : (null, "IMP-CONFIG-INVALID");
				}

				case ImportConfigurationValueKind.Choice:
				{
					var choice = item.Choices.FirstOrDefault(c => string.Equals(c, text, StringComparison.OrdinalIgnoreCase));
					return choice != null ? (choice, null) : (null, "IMP-CONFIG-INVALID");
				}

				case ImportConfigurationValueKind.Integer:
					if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
						return (null, "IMP-CONFIG-INVALID");
					return number < item.Min || number > item.Max ? (null, "IMP-CONFIG-OUT-OF-RANGE") : (number.ToString(CultureInfo.InvariantCulture), null);

				case ImportConfigurationValueKind.Coordinates:
				{
					var parts = text.Split(',');
					if (parts.Length != 2 ||
						!double.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude) ||
						!double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude))
						return (null, "IMP-CONFIG-INVALID");
					if (latitude < -90 || latitude > 90 || longitude < -180 || longitude > 180 || double.IsNaN(latitude) || double.IsNaN(longitude))
						return (null, "IMP-CONFIG-OUT-OF-RANGE");
					return (Coordinates(latitude, longitude), null);
				}

				case ImportConfigurationValueKind.Address:
				{
					var address = ReadJson<ImportConfigurationAddress>(text);
					if (address == null)
						return (null, "IMP-CONFIG-INVALID");
					var clean = ImportConfigurationCatalog.ReadJson<ImportConfigurationAddress>(AddressJson(address));
					if (clean.Address1 == null || clean.City == null || clean.State == null || clean.Country == null)
						return (null, "IMP-CONFIG-REQUIRED");
					if (clean.Address1.Length > 500 || clean.City.Length > 150 || clean.State.Length > 100 || clean.PostalCode?.Length > 32 || clean.Country.Length > 100)
						return (null, "IMP-CONFIG-TOO-LONG");
					return (AddressJson(clean), null);
				}

				case ImportConfigurationValueKind.NumberPattern:
				{
					if (item.Target == "calls")
					{
						var pattern = CallNumberFormat.Normalize(text);
						return pattern == null ? (null, "IMP-CONFIG-PATTERN") : (pattern, null);
					}
					if (item.Target == "records")
					{
						var pattern = RecordNumberFormat.Normalize(text);
						return pattern == null || pattern.Length > RecordNumberFormat.MaxPatternLength ? (null, "IMP-CONFIG-PATTERN") : (pattern, null);
					}

					var kind = DocumentNumberKinds.Find(item.Target);
					return kind != null && DocumentNumbering.IsValid(kind, text) ? (text, null) : (null, "IMP-CONFIG-PATTERN");
				}

				case ImportConfigurationValueKind.MonthDay:
				{
					var parts = text.Split('-');
					if (parts.Length != 2 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var month) ||
						!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var day) || !NumberingYearStart.IsValid(month, day))
						return (null, "IMP-CONFIG-INVALID");
					return (MonthDay(month, day), null);
				}

				case ImportConfigurationValueKind.Permission:
				{
					var permission = ReadJson<ImportConfigurationPermission>(text);
					if (permission == null || !PermissionActionChoices.Contains(permission.Action))
						return (null, "IMP-CONFIG-INVALID");
					if (permission.LockToGroup && !GroupScopedPermissions.Contains(item.Target))
						return (null, "IMP-CONFIG-INVALID");
					var roles = (permission.Roles ?? new List<int>()).Where(r => r > 0).Distinct().OrderBy(r => r).ToList();
					if (permission.Action == (int)PermissionActions.DepartmentAdminsAndSelectRoles)
					{
						if (roles.Count == 0)
							return (null, "IMP-CONFIG-ROLES-REQUIRED");
					}
					else
					{
						roles.Clear();
					}

					return (JsonConvert.SerializeObject(new ImportConfigurationPermission { Action = permission.Action, LockToGroup = permission.LockToGroup, Roles = roles }), null);
				}

				case ImportConfigurationValueKind.Logo:
					return Guid.TryParse(text, out _) ? (text.ToLowerInvariant(), null) : (null, "IMP-CONFIG-INVALID");

				default:
					return (null, "IMP-CONFIG-INVALID");
			}
		}

		/// <summary>
		/// An address in its canonical form (each part trimmed, empty parts absent). A department's current address reads
		/// back in this form even when it lacks a part a new one requires, so rollback can put it back exactly.
		/// </summary>
		public static string AddressJson(ImportConfigurationAddress address) =>
			JsonConvert.SerializeObject(new ImportConfigurationAddress
			{
				Address1 = Trim(address?.Address1), City = Trim(address?.City), State = Trim(address?.State), PostalCode = Trim(address?.PostalCode),
				Country = Trim(address?.Country)
			});

		public static string Coordinates(double latitude, double longitude) =>
			latitude.ToString("0.######", CultureInfo.InvariantCulture) + "," + longitude.ToString("0.######", CultureInfo.InvariantCulture);

		public static string MonthDay(int month, int day) =>
			month.ToString("00", CultureInfo.InvariantCulture) + "-" + day.ToString("00", CultureInfo.InvariantCulture);

		public static string Permission(int action, bool lockToGroup, IEnumerable<int> roles) =>
			JsonConvert.SerializeObject(new ImportConfigurationPermission
			{
				Action = action,
				LockToGroup = lockToGroup,
				Roles = action == (int)PermissionActions.DepartmentAdminsAndSelectRoles
					? (roles ?? Enumerable.Empty<int>()).Where(r => r > 0).Distinct().OrderBy(r => r).ToList()
					: new List<int>()
			});

		public static T ReadJson<T>(string json) where T : class
		{
			if (string.IsNullOrWhiteSpace(json))
				return null;
			try
			{
				return JsonConvert.DeserializeObject<T>(json);
			}
			catch (JsonException)
			{
				return null;
			}
		}

		private static string Trim(string value)
		{
			var trimmed = value?.Trim();
			return string.IsNullOrEmpty(trimmed) ? null : trimmed;
		}
	}
}
