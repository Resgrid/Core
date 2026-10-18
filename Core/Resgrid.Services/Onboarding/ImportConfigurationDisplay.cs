using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Resources;
using Resgrid.Localization;
using Resgrid.Model;
using Resgrid.Model.Onboarding;

namespace Resgrid.Services.Onboarding
{
	/// <summary>
	/// Labels and readable values for the Configure step (wizard plan section 6.3 step 9), shared by the department's
	/// Configure tab and the BackOffice configuration view so both say the same thing. Names come from the screens that own
	/// each setting where those already say it: the Security screen's permission and "who" labels, the Mapping screen's
	/// style names, the terminology catalog's default names and the operating profile's pack names. Each lookup returns
	/// null for a key it does not have.
	/// </summary>
	public sealed class ImportConfigurationDisplay
	{
		private static readonly IReadOnlyDictionary<string, string> PermissionLabels = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			[nameof(PermissionTypes.CreateCall)] = "PermCreateCallLabel",
			[nameof(PermissionTypes.CreateNote)] = "PermCreateNoteLabel",
			[nameof(PermissionTypes.ViewPersonalInfo)] = "PermViewPersonalInfoLabel",
			[nameof(PermissionTypes.ViewGroupUsers)] = "PermViewGroupsUsersLabel",
			[nameof(PermissionTypes.ViewGroupUnits)] = "PermViewGroupsUnitsLabel",
			[nameof(PermissionTypes.CanSeePersonnelLocations)] = "PermViewPersonnelLocationLabel",
			[nameof(PermissionTypes.CanSeeUnitLocations)] = "PermViewUnitLocationLabel"
		};

		private static readonly IReadOnlyDictionary<int, string> ActionLabels = new Dictionary<int, string>
		{
			[(int)PermissionActions.Everyone] = "PermOptionEveryone",
			[(int)PermissionActions.DepartmentAdminsOnly] = "PermOptionDepartmentAdmins",
			[(int)PermissionActions.DepartmentAndGroupAdmins] = "PermOptionDepartmentAndGroupAdmins",
			[(int)PermissionActions.DepartmentAdminsAndSelectRoles] = "PermOptionDepartmentAdminsAndSelectRoles"
		};

		private readonly Func<string, string> _import;
		private readonly Func<string, string> _security;
		private readonly Func<string, string> _department;
		private readonly Func<string, string> _assist;
		private readonly Func<string, string> _termName;
		private readonly IReadOnlyList<PersonnelRole> _roles;
		private readonly IReadOnlyList<ImportArtifact> _logos;
		private readonly IImportCatalog _catalog;

		/// <param name="import">DepartmentImport strings.</param>
		/// <param name="security">Security strings (permission and "who" labels).</param>
		/// <param name="department">Department strings (map style names).</param>
		/// <param name="assist">Admin Assist strings (pack names).</param>
		/// <param name="termName">A terminology term's default name in the reader's language.</param>
		public ImportConfigurationDisplay(Func<string, string> import, Func<string, string> security, Func<string, string> department, Func<string, string> assist,
			Func<string, string> termName, IReadOnlyList<PersonnelRole> roles, IReadOnlyList<ImportArtifact> logos, IImportCatalog catalog)
		{
			_import = import;
			_security = security;
			_department = department;
			_assist = assist;
			_termName = termName;
			_roles = roles ?? Array.Empty<PersonnelRole>();
			_logos = logos ?? Array.Empty<ImportArtifact>();
			_catalog = catalog;
		}

		/// <summary>The English display (BackOffice is English-only by convention): the en satellite resources Core ships.</summary>
		public static ImportConfigurationDisplay English(IReadOnlyList<PersonnelRole> roles, IReadOnlyList<ImportArtifact> logos, IImportCatalog catalog)
		{
			var english = CultureInfo.GetCultureInfo("en");
			Func<string, string> From(string baseName)
			{
				var resources = new ResourceManager(baseName, typeof(SupportedLocales).Assembly);
				return key =>
				{
					try
					{
						return resources.GetString(key, english);
					}
					catch (MissingManifestResourceException)
					{
						return null;
					}
				};
			}

			return new ImportConfigurationDisplay(From("Resgrid.Localization.Areas.User.DepartmentImport.DepartmentImport"),
				From("Resgrid.Localization.Areas.User.Security.Security"), From("Resgrid.Localization.Areas.User.Department.Department"),
				From("Resgrid.Localization.Areas.User.AdminAssist.AdminAssist"), key => DepartmentTerms.Find(key)?.DefaultName ?? key, roles, logos, catalog);
		}

		private string Import(string key) => _import(key) ?? key;

		public string Group(string group) => Import("ConfigGroup." + group);

		public string GroupHelp(string group) => Import("ConfigGroupHelp." + group);

		public string Label(ImportConfigurationItem item)
		{
			switch (item.Group)
			{
				case ImportConfigurationGroups.Terminology:
					return _termName(item.Target);
				case ImportConfigurationGroups.Modules:
					return Import("ConfigModule." + item.Target);
				case ImportConfigurationGroups.Access:
					return PermissionLabels.TryGetValue(item.Target, out var key) ? _security(key) ?? item.Target : item.Target;
				case ImportConfigurationGroups.Numbering:
				case ImportConfigurationGroups.Records:
				{
					var field = item.Key.Substring(item.Key.LastIndexOf('.') + 1);
					// A record type is named as the department names it ("Run · prefix"); the rest by kind ("Permits · next number").
					var subject = ImportConfigurationCatalog.NumberedRecordTypes.Contains(item.Target)
						? _termName(DepartmentTerms.RecordTypeKey(item.Target))
						: Import("ConfigKind." + item.Target);
					return subject + " · " + Import("ConfigField." + field);
				}
				default:
					return Import("Config." + item.Key);
			}
		}

		/// <summary>A canonical value as the department would say it; an empty one is the setting's default in words.</summary>
		public string Value(ImportConfigurationItem item, string value)
		{
			value ??= string.Empty;
			switch (item.Kind)
			{
				case ImportConfigurationValueKind.Text:
				case ImportConfigurationValueKind.Url:
					if (value.Length > 0)
						return value;
					return item.Group == ImportConfigurationGroups.Terminology
						? string.Format(CultureInfo.CurrentCulture, Import("ConfigResgridName"), _termName(item.Target))
						: Import("ConfigNotSet");

				case ImportConfigurationValueKind.Boolean:
					if (item.Group == ImportConfigurationGroups.Modules)
						return Import(value == "true" ? "ConfigOn" : "ConfigOff");
					return Import(value == "true" ? "ConfigYes" : "ConfigNo");

				case ImportConfigurationValueKind.TimeZone:
					return value.Length == 0 ? Import("ConfigNotSet") : TimeZones.Zones.TryGetValue(value, out var zone) ? zone : value;

				case ImportConfigurationValueKind.Choice:
					if (value.Length == 0 || value == ImportConfigurationCatalog.MapAutomatic)
						return Import("ConfigMapAutomatic");
					return _department("MapStyle_" + value) ?? value;

				case ImportConfigurationValueKind.Integer:
					if (value.Length == 0)
						return item.Group == ImportConfigurationGroups.Numbering || item.Group == ImportConfigurationGroups.Records ? Import("ConfigBuiltInNumbers") : Import("ConfigNotSet");
					return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number.ToString("N0", CultureInfo.CurrentCulture) : value;

				case ImportConfigurationValueKind.Coordinates:
					return value.Length == 0 ? Import("ConfigNotSet") : value.Replace(",", ", ");

				case ImportConfigurationValueKind.Address:
				{
					var address = ImportConfigurationCatalog.ReadJson<ImportConfigurationAddress>(value);
					if (address == null)
						return Import("ConfigNotSet");
					return string.Join(", ", new[] { address.Address1, address.City, string.Join(" ", new[] { address.State, address.PostalCode }.Where(p => !string.IsNullOrWhiteSpace(p))), address.Country }
						.Where(p => !string.IsNullOrWhiteSpace(p)));
				}

				case ImportConfigurationValueKind.NumberPattern:
					return value.Length == 0 ? Import("ConfigBuiltInNumbers") : value;

				case ImportConfigurationValueKind.NumberPrefix:
					return value.Length == 0 ? RmsDefinitionKeys.DefaultNumberPrefix(item.Target) : value;

				case ImportConfigurationValueKind.MonthDay:
				{
					var parts = value.Split('-');
					if (parts.Length == 2 && int.TryParse(parts[0], out var month) && int.TryParse(parts[1], out var day) && month >= 1 && month <= 12)
						return new DateTime(2001, month, Math.Min(Math.Max(day, 1), DateTime.DaysInMonth(2001, month))).ToString("M", CultureInfo.CurrentCulture);
					return value;
				}

				case ImportConfigurationValueKind.Permission:
				{
					var permission = ImportConfigurationCatalog.ReadJson<ImportConfigurationPermission>(value);
					if (permission == null)
						return ActionLabel((int)PermissionActions.Everyone);
					var text = ActionLabel(permission.Action);
					if (permission.Roles?.Count > 0)
						text += " (" + string.Join(", ", permission.Roles.Select(r => _roles.FirstOrDefault(x => x.PersonnelRoleId == r)?.Name ?? "#" + r)) + ")";
					if (permission.LockToGroup)
						text += " · " + Import("ConfigLockedToGroup");
					return text;
				}

				case ImportConfigurationValueKind.Logo:
				{
					if (value.Length == 0)
						return Import("ConfigNoLogo");
					if (value == ImportConfigurationTargets.LogoPresent)
						return Import("ConfigHasLogo");
					var logo = _logos.FirstOrDefault(l => string.Equals(l.Id, value, StringComparison.OrdinalIgnoreCase));
					return string.Format(CultureInfo.CurrentCulture, Import("ConfigLogoFile"), logo?.OriginalName ?? value);
				}

				default:
					return value;
			}
		}

		public string ActionLabel(int action) =>
			ActionLabels.TryGetValue(action, out var key) ? _security(key) ?? action.ToString(CultureInfo.InvariantCulture) : action.ToString(CultureInfo.InvariantCulture);

		public string Origin(ImportConfigurationOrigin origin) => Import("ConfigOrigin." + origin);

		/// <summary>What proposed it: the pack ("Search and rescue"), the data set ("for Points of interest"), the dispatch model or the products ("Aladtec").</summary>
		public string OriginDetail(ImportConfigurationOrigin origin, string originKey)
		{
			if (string.IsNullOrWhiteSpace(originKey))
				return null;
			if (originKey.StartsWith("pack.", StringComparison.Ordinal))
				return _assist("Pack." + originKey.Substring(5)) ?? originKey.Substring(5);
			if (originKey == "dispatch.external")
				return Import("ConfigOriginExternalDispatch");
			if (originKey.StartsWith("stations:", StringComparison.Ordinal) && int.TryParse(originKey.Substring(9), NumberStyles.Integer, CultureInfo.InvariantCulture, out var count))
				return string.Format(CultureInfo.CurrentCulture, Import("ConfigOriginStations"), count);
			if (originKey.StartsWith(ImportConfigurationService.ProductOriginPrefix, StringComparison.Ordinal))
				return string.Join(", ", originKey.Substring(ImportConfigurationService.ProductOriginPrefix.Length).Split(',', StringSplitOptions.RemoveEmptyEntries)
					.Select(k => _catalog?.GetPack(k)?.ProductName ?? k));
			var contract = _catalog?.GetContract(originKey);
			return contract == null ? null : string.Format(CultureInfo.CurrentCulture, Import("ConfigOriginDataset"), Import(contract.LabelKey));
		}

		/// <summary>An operating profile archetype's pack name ("Search and rescue").</summary>
		public string Pack(string archetype) => _assist("Pack." + archetype) ?? archetype;

		/// <summary>An operating profile field name ("WorkforceMix") or code ("volunteer") as Admin Assist labels it.</summary>
		public string Profile(string fieldOrCode) => _assist("Profile." + fieldOrCode) ?? fieldOrCode;

		public string Issue(string code) => string.IsNullOrWhiteSpace(code) ? null : _import("Error." + code) ?? Import("Error.Generic");

		public string State(ImportConfigurationChangeState state) => Import("ConfigState." + state switch
		{
			ImportConfigurationChangeState.Accepted => "Pending",
			ImportConfigurationChangeState.Rejected => "Rejected",
			ImportConfigurationChangeState.Applied => "Applied",
			ImportConfigurationChangeState.Failed => "Failed",
			ImportConfigurationChangeState.RolledBack => "RolledBack",
			_ => "Suggested"
		});
	}
}
