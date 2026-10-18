using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using NUnit.Framework;
using Resgrid.AdminAssist;
using Resgrid.Model;
using Resgrid.Model.AdminAssist;
using Resgrid.Model.Events;
using Resgrid.Model.Onboarding;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Providers.Import.Catalog;
using Resgrid.Services.AdminAssist;
using Resgrid.Services.Onboarding;
using Resgrid.Web.Areas.User.Controllers;
using Resgrid.Web.Areas.User.Models.DepartmentImport;
using Resgrid.Web.Areas.User.Models.Security;

namespace Resgrid.Tests.Onboarding
{
	/// <summary>
	/// The Configure step (wizard plan sections 6.3 step 9, 7.2, 7.5 and 8.1 A): the configuration catalog and its canonical
	/// values, suggestions composed from the operating profile and the data sets, the administrator's decisions, the
	/// commit-time write with the before-value journaled first, rollback while nothing changed since, and the digest.
	/// </summary>
	[TestFixture]
	public class ImportConfigurationTests : TestBase
	{
		private const int DepartmentId = 7;
		private const string WorkspaceId = "W1";
		private const string AdminId = "ADMIN";

		/// <summary>A configuration service with nothing pending, for fixtures that are about something else.</summary>
		public static IImportConfigurationService NoConfiguration()
		{
			var mock = new Mock<IImportConfigurationService>();
			mock.Setup(c => c.GetPendingAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ImportConfigurationPending());
			mock.Setup(c => c.ApplyAsync(It.IsAny<ImportRun>(), It.IsAny<string>(), It.IsAny<Func<ImportChangeJournalEntry, CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(new ImportConfigurationApplyResult());
			return mock.Object;
		}

		private CultureInfo _culture;

		[SetUp]
		public void SetUp()
		{
			_culture = CultureInfo.CurrentUICulture;
			CultureInfo.CurrentUICulture = new CultureInfo("en");
		}

		[TearDown]
		public void TearDown() => CultureInfo.CurrentUICulture = _culture;

		// ---- Catalog -------------------------------------------------------------------------------------------------

		[Test]
		public void Every_item_has_a_unique_key_in_a_known_group()
		{
			ImportConfigurationCatalog.All.Select(i => i.Key).Should().OnlyHaveUniqueItems();
			ImportConfigurationCatalog.All.Select(i => i.Group).Distinct().Should().BeSubsetOf(ImportConfigurationGroups.All);
			ImportConfigurationGroups.All.Should().OnlyContain(g => ImportConfigurationCatalog.All.Any(i => i.Group == g));
		}

		[Test]
		public void Module_and_term_items_name_real_switches_and_terms_and_every_Admin_Assist_id_exists()
		{
			foreach (var item in ImportConfigurationCatalog.All.Where(i => i.Group == ImportConfigurationGroups.Modules))
				typeof(DepartmentModuleSettings).GetProperty(item.Target + "Disabled").Should().NotBeNull(item.Key);
			ImportConfigurationCatalog.Modules.Should().NotContain("Ai", "AI is configured on its own screen (section 8.1 D)");

			ImportConfigurationCatalog.All.Where(i => i.Group == ImportConfigurationGroups.Terminology).Select(i => i.Target)
				.Should().BeEquivalentTo(DepartmentTerms.All.Select(t => t.Key));

			var assist = new ConfigurationCatalog();
			var ids = new HashSet<string>(assist.Settings.Select(s => s.Id), StringComparer.Ordinal);
			foreach (var item in ImportConfigurationCatalog.All.Where(i => i.AdminAssistId != null))
				ids.Should().Contain(item.AdminAssistId, item.Key);
		}

		[Test]
		public void Permission_items_are_the_ones_Admin_Assist_previews_with_the_actions_the_Security_screen_offers()
		{
			ImportConfigurationCatalog.Permissions.Should().BeEquivalentTo(PermissionImpactService.Supported);
			foreach (var name in ImportConfigurationCatalog.Permissions)
			{
				var entry = PermissionScreenCatalog.Get(Enum.Parse<PermissionTypes>(name));
				entry.Should().NotBeNull(name);
				entry.Actions.Should().Equal(ImportConfigurationCatalog.PermissionActionChoices, name);
				entry.LockToGroupOffered.Should().Be(ImportConfigurationCatalog.GroupScopedPermissions.Contains(name), name);
			}
		}

		[TestCase("terminology.module.units", "  Apparatus​  Bay ", "Apparatus Bay")]
		[TestCase("terminology.module.units", "", "")]
		[TestCase("department.use_24_hour_time", "Yes", "true")]
		[TestCase("department.time_zone", "eastern standard time", "Eastern Standard Time")]
		[TestCase("map.center", " 39.14080 , -119.7583 ", "39.1408,-119.7583")]
		[TestCase("map.day_style", "dark", "Dark")]
		[TestCase("map.zoom", "15", "15")]
		[TestCase("numbering.calls.pattern", " e-{yyyy}-{seq} ", "e-{YYYY}-{SEQ}")]
		[TestCase("numbering.calls.year_start", "7-1", "07-01")]
		[TestCase("numbering.work-order.pattern", "", "")]
		[TestCase("branding.website", "https://example.org", "https://example.org")]
		public void Values_have_one_canonical_form(string key, string raw, string expected)
		{
			var (value, code) = ImportConfigurationCatalog.Canonicalize(ImportConfigurationCatalog.Find(key), raw);
			code.Should().BeNull();
			value.Should().Be(expected);
		}

		[TestCase("terminology.module.units", "123456789012345678901234567890123456789012345678901", "IMP-CONFIG-TOO-LONG")]
		[TestCase("department.name", " ", "IMP-CONFIG-REQUIRED")]
		[TestCase("department.time_zone", "Mars/Olympus", "IMP-CONFIG-INVALID")]
		[TestCase("map.center", "95,10", "IMP-CONFIG-OUT-OF-RANGE")]
		[TestCase("map.zoom", "16", "IMP-CONFIG-OUT-OF-RANGE")]
		[TestCase("numbering.calls.pattern", "{YY}-{SEQ}-{SEQ}", "IMP-CONFIG-PATTERN")]
		[TestCase("numbering.calls.pattern", "", "IMP-CONFIG-REQUIRED")]
		[TestCase("numbering.calls.year_start", "02-29", "IMP-CONFIG-INVALID")]
		[TestCase("branding.website", "javascript:alert(1)", "IMP-CONFIG-INVALID")]
		[TestCase("branding.logo", "not-a-logo", "IMP-CONFIG-INVALID")]
		public void Values_that_cannot_be_written_are_refused_with_a_code(string key, string raw, string code)
		{
			ImportConfigurationCatalog.Canonicalize(ImportConfigurationCatalog.Find(key), raw).Code.Should().Be(code);
		}

		[Test]
		public void An_address_needs_street_city_region_and_country()
		{
			var item = ImportConfigurationCatalog.Find("department.address");
			ImportConfigurationCatalog.Canonicalize(item, JsonConvert.SerializeObject(new ImportConfigurationAddress { Address1 = "1 Main St", City = "Reno", Country = "US" }))
				.Code.Should().Be("IMP-CONFIG-REQUIRED");

			var (value, code) = ImportConfigurationCatalog.Canonicalize(item,
				JsonConvert.SerializeObject(new ImportConfigurationAddress { Address1 = " 1 Main St ", City = "Reno", State = "NV", Country = "US" }));
			code.Should().BeNull();
			JsonConvert.DeserializeObject<ImportConfigurationAddress>(value).Address1.Should().Be("1 Main St");
		}

		[Test]
		public void A_permission_keeps_roles_only_for_selected_roles_and_a_group_lock_only_where_it_means_something()
		{
			var call = ImportConfigurationCatalog.Find("permission.CreateCall");
			ImportConfigurationCatalog.Canonicalize(call, JsonConvert.SerializeObject(new ImportConfigurationPermission { Action = (int)PermissionActions.DepartmentAdminsAndSelectRoles }))
				.Code.Should().Be("IMP-CONFIG-ROLES-REQUIRED");
			ImportConfigurationCatalog.Canonicalize(call, JsonConvert.SerializeObject(new ImportConfigurationPermission { Action = (int)PermissionActions.Everyone, LockToGroup = true }))
				.Code.Should().Be("IMP-CONFIG-INVALID");
			ImportConfigurationCatalog.Canonicalize(call, JsonConvert.SerializeObject(new ImportConfigurationPermission { Action = (int)PermissionActions.DepartmentAndGroupAdminsAndSelectRoles }))
				.Code.Should().Be("IMP-CONFIG-INVALID", "the Security screen does not offer that action for these permissions");

			var (value, code) = ImportConfigurationCatalog.Canonicalize(call,
				JsonConvert.SerializeObject(new ImportConfigurationPermission { Action = (int)PermissionActions.Everyone, Roles = new List<int> { 4, 2 } }));
			code.Should().BeNull();
			value.Should().Be(ImportConfigurationCatalog.Permission((int)PermissionActions.Everyone, false, null));

			var units = ImportConfigurationCatalog.Find("permission.CanSeeUnitLocations");
			ImportConfigurationCatalog.Canonicalize(units, ImportConfigurationCatalog.Permission((int)PermissionActions.DepartmentAdminsAndSelectRoles, true, new[] { 4, 2, 4 }))
				.Value.Should().Be("{\"Action\":2,\"LockToGroup\":true,\"Roles\":[2,4]}");
		}

		[Test]
		public void The_digest_of_a_wave_without_configuration_is_the_data_set_digest_and_never_covers_state()
		{
			ImportConfigurationService.CombineDigest("abc", null).Should().Be("abc");
			ImportConfigurationService.CombineDigest("abc", new List<ImportConfigurationChange>()).Should().Be("abc");

			var change = new ImportConfigurationChange { Id = "C1", ItemKey = "terminology.module.units", Value = "Apparatus", State = (int)ImportConfigurationChangeState.Accepted };
			var digest = ImportConfigurationService.CombineDigest("abc", new[] { change });
			digest.Should().NotBe("abc");

			change.State = (int)ImportConfigurationChangeState.Applied;
			change.Revision = 9;
			ImportConfigurationService.CombineDigest("abc", new[] { change }).Should().Be(digest, "a slice that resumes after writing must still match its approval");

			change.Value = "Engines";
			ImportConfigurationService.CombineDigest("abc", new[] { change }).Should().NotBe(digest);
		}

		// ---- Fixture -------------------------------------------------------------------------------------------------

		private sealed class Fixture
		{
			public readonly Mock<IImportWorkspacesRepository> Workspaces = new();
			public readonly Mock<IImportDatasetsRepository> Datasets = new();
			public readonly Mock<IImportRunsRepository> Runs = new();
			public readonly Mock<IImportConfigurationChangesRepository> Changes = new();
			public readonly Mock<IImportArtifactsRepository> Artifacts = new();
			public readonly Mock<IDepartmentImportAccessService> Access = new();
			public readonly Mock<IDepartmentSettingsService> Settings = new();
			public readonly Mock<IAdminAssistCatalog> AssistCatalog = new();
			public readonly Mock<IAdminAssistAccessService> AssistAccess = new();
			public readonly Mock<IPersonnelRolesService> Roles = new();
			public readonly Mock<IDepartmentsService> Departments = new();
			public readonly Mock<IAddressService> Addresses = new();
			public readonly Mock<IDepartmentProfileMediaService> Profiles = new();
			public readonly Mock<ICallNumberingService> CallNumbering = new();
			public readonly Mock<IDocumentNumberingService> DocumentNumbering = new();
			public readonly Mock<IPermissionsService> Permissions = new();
			public readonly Mock<IEventAggregator> Events = new();
			public readonly Mock<IImportValidationService> Validation = new();
			public readonly Mock<IImportSourcesRepository> Sources = new();
			public readonly Mock<IFeatureToggleService> Features = new();
			public readonly Mock<IRmsOperationalRecordsRepository> Records = new();
			public bool RecordsOn = true;
			public RecordsNumberingConfig RecordNumbering = new();
			/// <summary>The highest sequence already issued, by the text before {SEQ} ("RUN-2026-").</summary>
			public readonly Dictionary<string, int> RecordsIssued = new(StringComparer.Ordinal);
			/// <summary>The products the workspace says it is moving from.</summary>
			public readonly List<ImportSource> Products = new();
			public readonly List<ImportCanonicalRow> StationRows = new();

			public readonly List<ImportConfigurationChange> Stored = new();
			public readonly List<ImportDataset> LiveDatasets = new();
			public readonly List<ImportRun> WorkspaceRuns = new();
			public readonly List<string> Calls = new();
			public DepartmentTerminologyConfig Terminology = new();
			public DepartmentModuleSettings Modules = new();
			public DepartmentOperatingProfile Profile = new() { Revision = 1, Archetypes = new List<string> { "sar" }, DispatchModel = "self" };
			public Permission CreateCall;

			public Fixture()
			{
				Workspaces.Setup(w => w.GetByIdAsync(DepartmentId, WorkspaceId, It.IsAny<CancellationToken>()))
					.ReturnsAsync(new ImportWorkspace { Id = WorkspaceId, DepartmentId = DepartmentId, Status = (int)ImportWorkspaceStatus.Collecting, CatalogVersion = ImportCatalog.Current.Version });
				Datasets.Setup(d => d.GetAllForWorkspaceAsync(DepartmentId, WorkspaceId, It.IsAny<CancellationToken>())).ReturnsAsync(() => LiveDatasets.ToList());
				Runs.Setup(r => r.GetAllForWorkspaceAsync(DepartmentId, WorkspaceId, It.IsAny<CancellationToken>())).ReturnsAsync(() => WorkspaceRuns.ToList());
				Artifacts.Setup(a => a.GetAllForWorkspaceAsync(DepartmentId, WorkspaceId, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ImportArtifact>());
				Access.Setup(a => a.CanManageAsync(DepartmentId, AdminId, false)).ReturnsAsync(true);
				AssistAccess.Setup(a => a.CanAccessAsync(It.IsAny<AdminAssistActor>(), false, It.IsAny<CancellationToken>())).ReturnsAsync(true);
				AssistCatalog.Setup(c => c.Packs).Returns(new ConfigurationCatalog().Packs);
				Roles.Setup(r => r.GetRolesForDepartmentAsync(DepartmentId)).ReturnsAsync(new List<PersonnelRole> { new PersonnelRole { PersonnelRoleId = 3, Name = "Officer", DepartmentId = DepartmentId } });

				Sources.Setup(s => s.GetAllForWorkspaceAsync(DepartmentId, WorkspaceId, It.IsAny<CancellationToken>())).ReturnsAsync(() => Products.ToList());

				// Records numbering through the real service: setting 72 in memory and the record numbers already issued.
				Features.Setup(f => f.IsEnabledAsync(FeatureFlagKeys.RecordsSystem, DepartmentId, It.IsAny<bool>(), It.IsAny<IDictionary<string, string>>()))
					.ReturnsAsync(() => RecordsOn);
				Settings.Setup(s => s.GetRecordsNumberingConfigAsync(DepartmentId, It.IsAny<bool>()))
					.ReturnsAsync(() => JsonConvert.DeserializeObject<RecordsNumberingConfig>(JsonConvert.SerializeObject(RecordNumbering)));
				Settings.Setup(s => s.SetRecordsNumberingConfigAsync(DepartmentId, It.IsAny<RecordsNumberingConfig>(), It.IsAny<CancellationToken>()))
					.Callback((int _, RecordsNumberingConfig config, CancellationToken __) => { Calls.Add("write:records-numbering"); RecordNumbering = config; })
					.ReturnsAsync(new DepartmentSetting());
				Records.Setup(r => r.GetMaxRecordNumberSequenceAsync(DepartmentId, It.IsAny<string>(), It.IsAny<string>()))
					.ReturnsAsync((int _, string prefix, string __) => RecordsIssued.TryGetValue(prefix, out var highest) ? highest : 0);

				// An in-memory decisions table.
				Changes.Setup(c => c.GetAllForWorkspaceAsync(DepartmentId, WorkspaceId, It.IsAny<CancellationToken>())).ReturnsAsync(() => Stored.ToList());
				Validation.Setup(v => v.ReadCanonicalRowsAsync(It.IsAny<ImportDataset>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync((ImportDataset _, int after, int take, CancellationToken __) => StationRows.Where(r => r.RowNumber > after).Take(take).ToList());
				Changes.Setup(c => c.GetForRunAsync(DepartmentId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync((int _, string run, CancellationToken __) => Stored.Where(s => s.ApprovedRunId == run).ToList());
				Changes.Setup(c => c.InsertAsync(It.IsAny<ImportConfigurationChange>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
					.ReturnsAsync((ImportConfigurationChange change, CancellationToken _, bool __) => { Stored.Add(change); return change; });
				Changes.Setup(c => c.UpdateWithRevisionAsync(It.IsAny<ImportConfigurationChange>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync((ImportConfigurationChange change, int revision, CancellationToken _) =>
					{
						var index = Stored.FindIndex(s => s.Id == change.Id);
						if (index < 0)
							return false;
						change.Revision = revision + 1;
						Stored[index] = change;
						return true;
					});
				Changes.Setup(c => c.DeleteAsync(It.IsAny<ImportConfigurationChange>(), It.IsAny<CancellationToken>()))
					.ReturnsAsync((ImportConfigurationChange change, CancellationToken _) => Stored.RemoveAll(s => s.Id == change.Id) > 0);

				// The department's settings.
				Settings.Setup(s => s.GetOperatingProfileAsync(DepartmentId)).ReturnsAsync(() => Profile);
				Settings.Setup(s => s.GetDepartmentTerminologyAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(() => Copy(Terminology));
				Settings.Setup(s => s.SetDepartmentTerminologyAsync(DepartmentId, It.IsAny<DepartmentTerminologyConfig>(), It.IsAny<CancellationToken>()))
					.Callback((int _, DepartmentTerminologyConfig config, CancellationToken __) => { Calls.Add("write:terminology"); Terminology = config; })
					.ReturnsAsync(new DepartmentSetting());
				Settings.Setup(s => s.GetDepartmentModuleSettingsAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(() => Copy(Modules));
				Settings.Setup(s => s.SetDepartmentModuleSettingsAsync(DepartmentId, It.IsAny<DepartmentModuleSettings>(), It.IsAny<CancellationToken>()))
					.Callback((int _, DepartmentModuleSettings settings, CancellationToken __) => { Calls.Add("write:modules"); Modules = settings; })
					.ReturnsAsync(new DepartmentSetting());
				Settings.Setup(s => s.GetMappingMapStyleAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(MapStyleTypes.Automatic);
				Settings.Setup(s => s.GetMappingMapStyleNightAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(MapStyleTypes.Automatic);
				Settings.Setup(s => s.GetCallNumberingConfigAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new CallNumberingConfig());
				Settings.Setup(s => s.GetDocumentNumberingConfigAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new DocumentNumberingConfig());
				Settings.Setup(s => s.GetDepartmentImportConfigAsync(DepartmentId, It.IsAny<bool>())).ReturnsAsync(new DepartmentImportConfig());
				CallNumbering.Setup(c => c.GetNextAsync(DepartmentId, It.IsAny<CallNumberingConfig>(), It.IsAny<DateTime>()))
					.ReturnsAsync(new CallNumberSequenceStatus { ScopeKey = "26-#", NextSequence = 42 });
				DocumentNumbering.Setup(d => d.GetStatusesAsync(DepartmentId, false, It.IsAny<DateTime>()))
					.ReturnsAsync(ImportConfigurationCatalog.DocumentKinds.Select(k => new DocumentNumberStatus { Kind = k }).ToList());
				Departments.Setup(d => d.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>()))
					.ReturnsAsync(new Department { DepartmentId = DepartmentId, Name = "Station 1", TimeZone = "Eastern Standard Time" });
				Profiles.Setup(p => p.GetBrandingAsync(DepartmentId)).ReturnsAsync(new DepartmentBranding { DepartmentId = DepartmentId });
				Permissions.Setup(p => p.GetPermissionByDepartmentTypeAsync(DepartmentId, It.IsAny<PermissionTypes>()))
					.ReturnsAsync((int _, PermissionTypes type) => type == PermissionTypes.CreateCall ? CreateCall : null);
				Permissions.Setup(p => p.SetPermissionForDepartmentAsync(DepartmentId, It.IsAny<string>(), It.IsAny<PermissionTypes>(), It.IsAny<PermissionActions>(),
						It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
					.Callback((int _, string __, PermissionTypes type, PermissionActions action, string data, bool locked, CancellationToken ___) =>
					{
						Calls.Add("write:permission");
						if (type == PermissionTypes.CreateCall)
							CreateCall = new Permission { PermissionType = (int)type, Action = (int)action, Data = data, LockToGroup = locked };
					})
					.ReturnsAsync(new Permission());
			}

			private static DepartmentTerminologyConfig Copy(DepartmentTerminologyConfig config) =>
				new DepartmentTerminologyConfig { Terms = config.Terms.Select(t => new DepartmentTermOverride { Key = t.Key, Text = t.Text }).ToList() };

			private static DepartmentModuleSettings Copy(DepartmentModuleSettings modules) =>
				JsonConvert.DeserializeObject<DepartmentModuleSettings>(JsonConvert.SerializeObject(modules));

			public ImportConfigurationTargets Targets() => new(Departments.Object, Addresses.Object, Settings.Object, Profiles.Object, CallNumbering.Object,
				DocumentNumbering.Object, Permissions.Object, Artifacts.Object, Mock.Of<IImportArtifactStore>(), Mock.Of<IImportArtifactCipher>(), Mock.Of<IImportKeyService>(),
				new Resgrid.Services.Records.RecordsNumberingService(Settings.Object, Records.Object, Mock.Of<IDepartmentGroupsService>()), Features.Object);

			/// <summary>The numbering year a record started now falls in, as the department (Eastern time) reads it.</summary>
			public int RecordsYear => Resgrid.Services.Records.RecordsNumberingService.NumberingYear(RecordNumbering, DateTime.UtcNow, "Eastern Standard Time");

			public ImportEffectsRunner Effects() => new(Events.Object, Mock.Of<IDepartmentsService>(), Mock.Of<IUsersService>(), Mock.Of<IUserProfileService>(),
				Mock.Of<ILimitsService>(), Mock.Of<ICustomStateService>(), Mock.Of<ICallsService>(), Mock.Of<ISearchIndexMaintenanceService>(),
				Mock.Of<ISystemOperationsService>());

			public ImportConfigurationService Service() => new(Workspaces.Object, Datasets.Object, Runs.Object, Changes.Object, Artifacts.Object, ImportCatalog.Current,
				Access.Object, Settings.Object, AssistCatalog.Object, AssistAccess.Object, Roles.Object, Targets(), Mock.Of<IImportTimelineService>(), Effects(),
				new Lazy<IImportValidationService>(() => Validation.Object), Sources.Object);

			public void Choose(params string[] productKeys) =>
				Products.AddRange(productKeys.Select((key, i) => new ImportSource
				{
					Id = "S" + (Products.Count + i), DepartmentId = DepartmentId, WorkspaceId = WorkspaceId, ProductKey = key, CreatedOn = DateTime.UtcNow.AddMinutes(Products.Count + i)
				}));

			public static ImportConfigurationRowView Row(ImportConfigurationView view, string key) => view.Groups.SelectMany(g => g.Rows).Single(r => r.Item.Key == key);
		}

		// ---- Suggestions ---------------------------------------------------------------------------------------------

		[Test]
		public async Task A_profile_suggests_the_modules_its_packs_work_in_and_the_names_its_kind_of_department_uses()
		{
			var fixture = new Fixture();
			fixture.Modules.ShiftsDisabled = true;

			var view = await fixture.Service().GetAsync(DepartmentId, AdminId, WorkspaceId);

			var shifts = Fixture.Row(view, "modules.Shifts");
			shifts.Suggested.Should().Be("true");
			shifts.SuggestedOrigin.Should().Be(ImportConfigurationOrigin.Preset);
			shifts.SuggestedOriginKey.Should().Be("pack.sar");
			shifts.AwaitingDecision.Should().BeTrue();
			Fixture.Row(view, "modules.Mapping").Suggested.Should().BeNull("the module is already on");

			Fixture.Row(view, "terminology." + DepartmentTerms.Calls).Suggested.Should().Be("Missions");
			Fixture.Row(view, "terminology." + DepartmentTerms.Units).Suggested.Should().Be("Teams");
			Fixture.Row(view, "permission.CreateCall").Suggested.Should().BeNull("the department dispatches itself");
			view.Profile.Should().NotBeNull();
		}

		[Test]
		public async Task Names_are_suggested_only_where_the_profile_agrees_and_never_over_a_department_name()
		{
			var fixture = new Fixture();
			fixture.Profile.Archetypes = new List<string> { "sar", "security" };
			fixture.Terminology.Set(DepartmentTerms.Units, "Apparatus");

			var view = await fixture.Service().GetAsync(DepartmentId, AdminId, WorkspaceId);

			Fixture.Row(view, "terminology." + DepartmentTerms.Calls).Suggested.Should().BeNull("search and rescue and security name calls differently");
			Fixture.Row(view, "terminology." + DepartmentTerms.Personnel).Suggested.Should().Be("Officers");
			Fixture.Row(view, "terminology." + DepartmentTerms.Units).Suggested.Should().BeNull("the department already named its units");
		}

		[Test]
		public async Task Without_a_saved_profile_nothing_is_suggested_from_it()
		{
			var fixture = new Fixture { Profile = new DepartmentOperatingProfile() };
			fixture.Modules.ShiftsDisabled = true;

			var view = await fixture.Service().GetAsync(DepartmentId, AdminId, WorkspaceId);

			view.Profile.Should().BeNull();
			view.Suggestions.Should().Be(0);
		}

		[Test]
		public async Task Under_an_upstream_CAD_call_creation_is_suggested_for_admins_and_the_data_sets_ask_for_their_modules()
		{
			var fixture = new Fixture();
			fixture.Profile.DispatchModel = "external";
			fixture.Modules.MappingDisabled = true;
			fixture.LiveDatasets.Add(new ImportDataset { Id = "D1", WorkspaceId = WorkspaceId, EntityKey = "pois", Status = (int)ImportDatasetStatus.Ready });

			var view = await fixture.Service().GetAsync(DepartmentId, AdminId, WorkspaceId);

			var call = Fixture.Row(view, "permission.CreateCall");
			ImportConfigurationCatalog.ReadJson<ImportConfigurationPermission>(call.Suggested).Action.Should().Be((int)PermissionActions.DepartmentAndGroupAdmins);
			call.SuggestedOriginKey.Should().Be("dispatch.external");

			var mapping = Fixture.Row(view, "modules.Mapping");
			mapping.Suggested.Should().Be("true");
			mapping.SuggestedOrigin.Should().Be(ImportConfigurationOrigin.ImportedData, "the imported data is the stronger reason");
			mapping.SuggestedOriginKey.Should().Be("pois");
		}

		// ---- The products the department is moving from (pack glossaries, plan sections 7.4 and 7.6) -------------------

		[Test]
		public async Task A_product_the_department_is_moving_from_suggests_its_word_over_the_presets()
		{
			var fixture = new Fixture();
			fixture.Choose("aladtec", "first-due");

			var view = await fixture.Service().GetAsync(DepartmentId, AdminId, WorkspaceId);

			var personnel = Fixture.Row(view, "terminology." + DepartmentTerms.Personnel);
			personnel.Suggested.Should().Be("Members");
			personnel.SuggestedOrigin.Should().Be(ImportConfigurationOrigin.Product);
			personnel.SuggestedOriginKey.Should().Be("product.aladtec");
			var units = Fixture.Row(view, "terminology." + DepartmentTerms.Units);
			units.Suggested.Should().Be("Apparatus", "the product the department used is more specific than its kind of department ('Teams')");
			units.SuggestedOriginKey.Should().Be("product.first-due");
			Fixture.Row(view, "terminology." + DepartmentTerms.Calls).SuggestedOrigin.Should().Be(ImportConfigurationOrigin.Preset, "neither product names calls");

			// Accepting keeps where it came from.
			var change = await fixture.Service().DecideAsync(DepartmentId, AdminId, ImportActorKind.Administrator, WorkspaceId,
				new ImportConfigurationDecision { ItemKey = "terminology." + DepartmentTerms.Personnel, UseSuggestion = true });
			change.Value.Should().Be("Members");
			change.Origin.Should().Be((int)ImportConfigurationOrigin.Product);

			var display = ImportConfigurationDisplay.English(null, null, ImportCatalog.Current);
			display.Origin(ImportConfigurationOrigin.Product).Should().Be("Suggested from the products you are moving from");
			display.OriginDetail(ImportConfigurationOrigin.Product, "product.aladtec,d4h").Should().Be("Aladtec, D4H Team Manager");
		}

		[Test]
		public async Task Products_that_name_a_feature_differently_leave_it_to_the_preset()
		{
			var fixture = new Fixture();
			fixture.Choose("first-due", "divera247", "aladtec", "d4h");

			var view = await fixture.Service().GetAsync(DepartmentId, AdminId, WorkspaceId);

			var units = Fixture.Row(view, "terminology." + DepartmentTerms.Units);
			units.Suggested.Should().Be("Teams", "First Due says Apparatus and DIVERA 24/7 says Vehicles");
			units.SuggestedOrigin.Should().Be(ImportConfigurationOrigin.Preset);
			var personnel = Fixture.Row(view, "terminology." + DepartmentTerms.Personnel);
			personnel.Suggested.Should().Be("Members");
			personnel.SuggestedOriginKey.Should().Be("product.aladtec,d4h");
			Fixture.Row(view, "terminology." + DepartmentTerms.Calls).Suggested.Should().Be("Incidents", "D4H names calls; no other product does");
		}

		[Test]
		public async Task A_products_word_is_offered_only_in_its_own_language_and_never_over_the_departments_name()
		{
			var fixture = new Fixture();
			fixture.Profile = new DepartmentOperatingProfile();
			fixture.Choose("divera247", "gartan");

			CultureInfo.CurrentUICulture = new CultureInfo("de");
			Fixture.Row(await fixture.Service().GetAsync(DepartmentId, AdminId, WorkspaceId), "terminology." + DepartmentTerms.Units).Suggested.Should().Be("Fahrzeuge");

			CultureInfo.CurrentUICulture = new CultureInfo("fr");
			Fixture.Row(await fixture.Service().GetAsync(DepartmentId, AdminId, WorkspaceId), "terminology." + DepartmentTerms.Units).Suggested
				.Should().BeNull("the department's name shows in every language, so an English or German word is not offered to a French reader");

			CultureInfo.CurrentUICulture = new CultureInfo("en");
			Fixture.Row(await fixture.Service().GetAsync(DepartmentId, AdminId, WorkspaceId), "terminology." + DepartmentTerms.Units).Suggested.Should().Be("Vehicles");
			fixture.Terminology = new DepartmentTerminologyConfig { Terms = { new DepartmentTermOverride { Key = DepartmentTerms.Units, Text = "Crews" } } };
			Fixture.Row(await fixture.Service().GetAsync(DepartmentId, AdminId, WorkspaceId), "terminology." + DepartmentTerms.Units).Suggested.Should().BeNull();
		}

		[Test]
		public void The_glossary_names_where_each_word_lands_as_the_department_says_it()
		{
			var glossary = new ImportGlossaryDisplay(key => key switch
			{
				"Entity.personnel" => "People",
				"GlossaryNoEquivalent" => "Not imported",
				_ => null
			}, key => key == DepartmentTerms.Shifts ? "Rosters" : DepartmentTerms.Find(key)?.DefaultName, ImportCatalog.Current);
			var aladtec = ImportCatalog.Current.GetPack("aladtec");

			var rows = glossary.Rows(aladtec, "en");

			rows.Single(r => r.Term == "Members").Should().BeEquivalentTo(new { Resgrid = "People", Imported = true, Equivalent = true, OfferedAsName = true });
			rows.Single(r => r.Term == "Schedules").Should().BeEquivalentTo(new { Resgrid = "Rosters", Imported = false, Equivalent = true, OfferedAsName = false });
			rows.Single(r => r.Term == "Payroll").Should().BeEquivalentTo(new { Resgrid = "Not imported", Imported = false, Equivalent = false });
			glossary.Rows(aladtec, "fr").Single(r => r.Term == "Members").OfferedAsName.Should().BeFalse("only a word in the reader's language is offered as a name");

			var english = ImportGlossaryDisplay.English(ImportCatalog.Current);
			english.Status(aladtec).Should().Be("Glossary available");
			english.Status(ImportCatalog.Current.GetPack("gartan")).Should().Be("No direct import — use a spreadsheet");
			english.Products(new[]
			{
				new ImportSource { ProductKey = ImportSourceProducts.Continuation },
				new ImportSource { ProductKey = "d4h", CreatedOn = new DateTime(2026, 10, 2) },
				new ImportSource { ProductKey = "aladtec", CreatedOn = new DateTime(2026, 10, 1) }
			}).Select(p => p.Key).Should().Equal("aladtec", "d4h");
		}

		// ---- Decisions -----------------------------------------------------------------------------------------------

		[Test]
		public async Task Decisions_keep_the_origin_of_the_value_and_a_rejection_stops_the_suggestion_waiting()
		{
			var fixture = new Fixture();
			fixture.Modules.ShiftsDisabled = true;
			var service = fixture.Service();

			var suggestion = await service.DecideAsync(DepartmentId, AdminId, ImportActorKind.Administrator, WorkspaceId,
				new ImportConfigurationDecision { ItemKey = "terminology." + DepartmentTerms.Calls, UseSuggestion = true });
			suggestion.Origin.Should().Be((int)ImportConfigurationOrigin.Preset);
			suggestion.Value.Should().Be("Missions");
			suggestion.State.Should().Be((int)ImportConfigurationChangeState.Accepted);

			var own = await service.DecideAsync(DepartmentId, AdminId, ImportActorKind.Administrator, WorkspaceId,
				new ImportConfigurationDecision { ItemKey = "terminology." + DepartmentTerms.Units, Value = " Apparatus " });
			own.Origin.Should().Be((int)ImportConfigurationOrigin.Administrator);
			own.Value.Should().Be("Apparatus");

			await service.DecideAsync(DepartmentId, AdminId, ImportActorKind.Administrator, WorkspaceId, new ImportConfigurationDecision { ItemKey = "modules.Shifts", Accept = false });
			var view = await service.GetAsync(DepartmentId, AdminId, WorkspaceId);
			Fixture.Row(view, "modules.Shifts").AwaitingDecision.Should().BeFalse();
			Fixture.Row(view, "modules.Shifts").Change.State.Should().Be((int)ImportConfigurationChangeState.Rejected);
			Fixture.Row(view, "modules.Shifts").Pending.Should().BeFalse();
			Fixture.Row(view, "terminology." + DepartmentTerms.Units).Pending.Should().BeTrue();
			view.Pending.Should().Be(2);

			fixture.Stored.Should().HaveCount(3, "one row per item");
		}

		[Test]
		public async Task Nothing_changes_under_an_open_run_and_a_value_that_cannot_be_written_is_refused()
		{
			var fixture = new Fixture();
			var service = fixture.Service();

			var refused = await FluentActions.Invoking(() => service.DecideAsync(DepartmentId, AdminId, ImportActorKind.Administrator, WorkspaceId,
				new ImportConfigurationDecision { ItemKey = "terminology." + DepartmentTerms.Units, Value = new string('x', 51) })).Should().ThrowAsync<DepartmentImportException>();
			refused.Which.Code.Should().Be("IMP-CONFIG-TOO-LONG");

			fixture.WorkspaceRuns.Add(new ImportRun { Id = "R1", WorkspaceId = WorkspaceId, Kind = (int)ImportRunKind.Commit, Status = (int)ImportRunStatus.AwaitingChecker });
			var locked = await FluentActions.Invoking(() => service.DecideAsync(DepartmentId, AdminId, ImportActorKind.Administrator, WorkspaceId,
				new ImportConfigurationDecision { ItemKey = "terminology." + DepartmentTerms.Units, Value = "Apparatus" })).Should().ThrowAsync<DepartmentImportException>();
			locked.Which.Code.Should().Be("import_run_open");
			(await service.GetAsync(DepartmentId, AdminId, WorkspaceId)).CanEdit.Should().BeFalse();
		}

		[Test]
		public async Task A_permission_needs_Admin_Assist_to_preview_it_and_accepting_all_skips_one_that_cannot_be_previewed()
		{
			var fixture = new Fixture();
			fixture.Profile.DispatchModel = "external";
			fixture.Modules.ShiftsDisabled = true;
			fixture.AssistAccess.Setup(a => a.CanAccessAsync(It.IsAny<AdminAssistActor>(), false, It.IsAny<CancellationToken>())).ReturnsAsync(false);
			var service = fixture.Service();

			var refused = await FluentActions.Invoking(() => service.DecideAsync(DepartmentId, AdminId, ImportActorKind.Administrator, WorkspaceId,
				new ImportConfigurationDecision { ItemKey = "permission.CreateCall", UseSuggestion = true })).Should().ThrowAsync<DepartmentImportException>();
			refused.Which.Code.Should().Be("IMP-CONFIG-PREVIEW-UNAVAILABLE");

			var accepted = await service.AcceptSuggestionsAsync(DepartmentId, AdminId, ImportActorKind.Administrator, WorkspaceId, null);
			accepted.Should().BeGreaterThan(0);
			fixture.Stored.Should().NotContain(s => s.ItemKey == "permission.CreateCall");
			fixture.Stored.Should().Contain(s => s.ItemKey == "modules.Shifts");
		}

		[Test]
		public async Task Roles_of_a_permission_must_be_the_department_s_own()
		{
			var fixture = new Fixture();
			var service = fixture.Service();

			var refused = await FluentActions.Invoking(() => service.DecideAsync(DepartmentId, AdminId, ImportActorKind.Administrator, WorkspaceId, new ImportConfigurationDecision
			{
				ItemKey = "permission.CreateNote",
				Value = ImportConfigurationCatalog.Permission((int)PermissionActions.DepartmentAdminsAndSelectRoles, false, new[] { 99 })
			})).Should().ThrowAsync<DepartmentImportException>();
			refused.Which.Code.Should().Be("IMP-CONFIG-ROLES-MISSING");

			await service.DecideAsync(DepartmentId, AdminId, ImportActorKind.Administrator, WorkspaceId, new ImportConfigurationDecision
			{
				ItemKey = "permission.CreateNote",
				Value = ImportConfigurationCatalog.Permission((int)PermissionActions.DepartmentAdminsAndSelectRoles, false, new[] { 3 })
			});
			fixture.Stored.Should().ContainSingle(s => s.ItemKey == "permission.CreateNote");
		}

		[Test]
		public async Task Pending_is_what_differs_from_the_department_and_says_what_cannot_be_written_as_it_stands()
		{
			var fixture = new Fixture();
			var service = fixture.Service();
			await service.DecideAsync(DepartmentId, AdminId, ImportActorKind.Administrator, WorkspaceId, new ImportConfigurationDecision { ItemKey = "department.name", Value = "Station 1" });
			await service.DecideAsync(DepartmentId, AdminId, ImportActorKind.Administrator, WorkspaceId, new ImportConfigurationDecision { ItemKey = "numbering.invoice.next", Value = "500" });
			await service.DecideAsync(DepartmentId, AdminId, ImportActorKind.Administrator, WorkspaceId, new ImportConfigurationDecision { ItemKey = "numbering.calls.next", Value = "40" });
			await service.DecideAsync(DepartmentId, AdminId, ImportActorKind.Administrator, WorkspaceId, new ImportConfigurationDecision { ItemKey = "numbering.bid.pattern", Value = "BID-{YYYY}-{SEQ}" });
			await service.DecideAsync(DepartmentId, AdminId, ImportActorKind.Administrator, WorkspaceId, new ImportConfigurationDecision { ItemKey = "numbering.bid.next", Value = "10" });

			var pending = await service.GetPendingAsync(DepartmentId, WorkspaceId);

			pending.Changes.Select(c => c.ItemKey).Should().BeEquivalentTo("numbering.invoice.next", "numbering.calls.next", "numbering.bid.pattern", "numbering.bid.next");
			pending.Issues.Should().BeEquivalentTo(new Dictionary<string, string>
			{
				["numbering.invoice.next"] = "IMP-CONFIG-NEEDS-PATTERN",
				["numbering.calls.next"] = "IMP-CONFIG-BELOW-CURRENT"
			}, "a next number needs its own pattern, and calls already issue 42 next");
			pending.Decided.Should().Be(5);
		}

		// ---- Map centre from the imported stations ---------------------------------------------------------------------

		[Test]
		public void The_middle_is_the_median_position_and_crosses_the_180th_meridian_the_short_way()
		{
			ImportConfigurationService.Middle(new[] { (39.5, -119.8), (39.6, -119.7), (39.4, -119.9) }).Should().Be((39.5, -119.8));
			ImportConfigurationService.Middle(new[] { (39.5, -119.8), (39.6, -119.7), (39.4, -119.9), (51.5, -0.12) })
				.Should().Be((39.55, -119.75), "one station on the wrong continent moves the middle a little, not to the ocean");
			ImportConfigurationService.Middle(new[] { (-17.8, 178.0), (-17.7, -179.0), (-17.9, 179.0) }).Should().Be((-17.8, 179.0));
			ImportConfigurationService.Middle(new[] { (-16.5, 179.5), (-16.6, -179.5) }).Should().Be((-16.55, 180.0));
		}

		private static ImportCanonicalRow Station(int row, string latitude, string longitude, string type = "station",
			ImportRowValidationState state = ImportRowValidationState.Valid, ImportRowDecision decision = ImportRowDecision.Create)
		{
			var canonical = new ImportCanonicalRow { RowNumber = row, ValidationState = state, Decision = decision };
			canonical.Values["latitude"] = latitude;
			canonical.Values["longitude"] = longitude;
			if (type != null)
				canonical.Values["type"] = type;
			return canonical;
		}

		[Test]
		public async Task Without_a_map_centre_the_middle_of_the_imported_stations_is_suggested()
		{
			var fixture = new Fixture();
			fixture.LiveDatasets.Add(new ImportDataset { Id = "G1", WorkspaceId = WorkspaceId, EntityKey = "groups", Status = (int)ImportDatasetStatus.Ready });
			fixture.StationRows.AddRange(new[]
			{
				Station(2, "39.5", "-119.8"), Station(3, "39.6", "-119.7"), Station(4, "39.4", "-119.9", decision: ImportRowDecision.Link),
				Station(5, "45.0", "-100.0", type: "organizational"),
				Station(6, "10.0", "10.0", state: ImportRowValidationState.Blocked),
				Station(7, "11.0", "11.0", state: ImportRowValidationState.Excluded),
				Station(8, "12.0", "12.0", decision: ImportRowDecision.Skip),
				Station(9, "0", "0"),
				Station(10, "95", "10")
			});

			var view = await fixture.Service().GetAsync(DepartmentId, AdminId, WorkspaceId);

			var centre = Fixture.Row(view, "map.center");
			centre.Suggested.Should().Be("39.5,-119.8", "the three stations that will be (or already are) the department's");
			centre.SuggestedOrigin.Should().Be(ImportConfigurationOrigin.ImportedData);
			centre.SuggestedOriginKey.Should().Be("stations:3");
			ImportConfigurationDisplay.English(null, null, ImportCatalog.Current).OriginDetail(ImportConfigurationOrigin.ImportedData, centre.SuggestedOriginKey)
				.Should().Be("the middle of the 3 stations you are importing");

			var accepted = await fixture.Service().DecideAsync(DepartmentId, AdminId, ImportActorKind.Administrator, WorkspaceId,
				new ImportConfigurationDecision { ItemKey = "map.center", UseSuggestion = true });
			accepted.Value.Should().Be("39.5,-119.8");
			accepted.OriginKey.Should().Be("stations:3");
		}

		[Test]
		public async Task Groups_without_a_station_type_still_place_the_map_and_a_department_centre_is_never_replaced()
		{
			var fixture = new Fixture();
			fixture.LiveDatasets.Add(new ImportDataset { Id = "G1", WorkspaceId = WorkspaceId, EntityKey = "groups", Status = (int)ImportDatasetStatus.Ready });
			fixture.StationRows.AddRange(new[] { Station(2, "-33.86", "151.2", type: null), Station(3, "-33.9", "151.1", type: null) });

			Fixture.Row(await fixture.Service().GetAsync(DepartmentId, AdminId, WorkspaceId), "map.center").Suggested.Should().Be("-33.88,151.15");

			fixture.Settings.Setup(s => s.GetBigBoardCenterGpsCoordinatesDepartmentAsync(DepartmentId)).ReturnsAsync("40.0,-75.0");
			Fixture.Row(await fixture.Service().GetAsync(DepartmentId, AdminId, WorkspaceId), "map.center").Suggested.Should().BeNull();
		}

		[Test]
		public async Task Staff_below_Inspect_never_get_a_suggestion_worked_out_from_staged_values_and_a_purged_file_suggests_nothing()
		{
			var fixture = new Fixture();
			fixture.LiveDatasets.Add(new ImportDataset { Id = "G1", WorkspaceId = WorkspaceId, EntityKey = "groups", Status = (int)ImportDatasetStatus.Ready });
			fixture.StationRows.Add(Station(2, "39.5", "-119.8"));
			var service = fixture.Service();

			Fixture.Row(await service.GetForStaffAsync(DepartmentId, WorkspaceId, false), "map.center").Suggested.Should().BeNull();
			fixture.Validation.Verify(v => v.ReadCanonicalRowsAsync(It.IsAny<ImportDataset>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
			Fixture.Row(await service.GetForStaffAsync(DepartmentId, WorkspaceId, true), "map.center").Suggested.Should().Be("39.5,-119.8");

			fixture.Validation.Setup(v => v.ReadCanonicalRowsAsync(It.IsAny<ImportDataset>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
				.ThrowsAsync(new ImportArtifactIntegrityException("IMP-ARTIFACT-MISSING"));
			Fixture.Row(await service.GetAsync(DepartmentId, AdminId, WorkspaceId), "map.center").Suggested.Should().BeNull();
		}

		// ---- Resgrid staff (plan 17) ----------------------------------------------------------------------------------

		private const string StaffId = "staff:support@example.test";

		[Test]
		public async Task Staff_drafts_are_labelled_as_theirs_never_touch_permissions_and_never_override_the_department()
		{
			var fixture = new Fixture();
			fixture.Profile.DispatchModel = "external";
			var service = fixture.Service();

			var draft = await service.DecideAsync(DepartmentId, StaffId, ImportActorKind.ResgridStaff, WorkspaceId,
				new ImportConfigurationDecision { ItemKey = "terminology." + DepartmentTerms.Units, Value = "Apparatus" });
			draft.StaffAuthored.Should().BeTrue();
			draft.Origin.Should().Be((int)ImportConfigurationOrigin.Staff);
			draft.UpdatedBy.Should().Be(StaffId);

			var accepted = await service.DecideAsync(DepartmentId, StaffId, ImportActorKind.ResgridStaff, WorkspaceId,
				new ImportConfigurationDecision { ItemKey = "terminology." + DepartmentTerms.Calls, UseSuggestion = true });
			accepted.Origin.Should().Be((int)ImportConfigurationOrigin.Preset, "accepting a suggestion keeps where it came from");
			accepted.StaffAuthored.Should().BeTrue();

			(await FluentActions.Invoking(() => service.DecideAsync(DepartmentId, StaffId, ImportActorKind.ResgridStaff, WorkspaceId,
				new ImportConfigurationDecision { ItemKey = "permission.CreateCall", UseSuggestion = true })).Should().ThrowAsync<DepartmentImportException>())
				.Which.Code.Should().Be("IMP-CONFIG-STAFF-PERMISSION");

			await service.DecideAsync(DepartmentId, AdminId, ImportActorKind.Administrator, WorkspaceId,
				new ImportConfigurationDecision { ItemKey = "department.name", Value = "Station One" });
			(await FluentActions.Invoking(() => service.DecideAsync(DepartmentId, StaffId, ImportActorKind.ResgridStaff, WorkspaceId,
				new ImportConfigurationDecision { ItemKey = "department.name", Value = "Station 1 FD" })).Should().ThrowAsync<DepartmentImportException>())
				.Which.Code.Should().Be("IMP-CONFIG-DEPARTMENT-DECISION");
			(await FluentActions.Invoking(() => service.ResetAsync(DepartmentId, StaffId, ImportActorKind.ResgridStaff, WorkspaceId, "department.name"))
				.Should().ThrowAsync<DepartmentImportException>()).Which.Code.Should().Be("IMP-CONFIG-NOT-STAFF-DECISION");

			// The department deciding over a staff draft makes it the department's own; staff may still undo their other drafts.
			var owned = await service.DecideAsync(DepartmentId, AdminId, ImportActorKind.Administrator, WorkspaceId,
				new ImportConfigurationDecision { ItemKey = "terminology." + DepartmentTerms.Units, Value = "Apparatus" });
			owned.StaffAuthored.Should().BeFalse();
			(await service.ResetAsync(DepartmentId, StaffId, ImportActorKind.ResgridStaff, WorkspaceId, "terminology." + DepartmentTerms.Calls)).Should().BeTrue();
		}

		[Test]
		public async Task Staff_see_the_same_view_without_a_permission_preview_and_bulk_accept_skips_permissions()
		{
			var fixture = new Fixture();
			fixture.Profile.DispatchModel = "external";
			fixture.Modules.ShiftsDisabled = true;
			var service = fixture.Service();

			var view = await service.GetForStaffAsync(DepartmentId, WorkspaceId, false);
			view.PermissionPreviewAvailable.Should().BeFalse();
			view.Suggestions.Should().BeGreaterThan(1);
			fixture.AssistAccess.Verify(a => a.CanAccessAsync(It.IsAny<AdminAssistActor>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);

			var accepted = await service.AcceptSuggestionsAsync(DepartmentId, StaffId, ImportActorKind.ResgridStaff, WorkspaceId, null);
			accepted.Should().Be(view.Suggestions - 1, "the Create Calls suggestion waits for the department");
			fixture.Stored.Should().OnlyContain(s => s.StaffAuthored).And.NotContain(s => s.ItemKey == "permission.CreateCall");

			(await FluentActions.Invoking(() => service.DecideAsync(DepartmentId, "anyone", ImportActorKind.System, WorkspaceId,
				new ImportConfigurationDecision { ItemKey = "modules.Shifts", Value = "true" })).Should().ThrowAsync<DepartmentImportException>())
				.Which.Code.Should().Be("import_not_allowed");
		}

		// ---- Commit and rollback -------------------------------------------------------------------------------------

		private static ImportRun Run() => new ImportRun { Id = "RUN", DepartmentId = DepartmentId, WorkspaceId = WorkspaceId, Kind = (int)ImportRunKind.Commit };

		private static async Task<ImportConfigurationChange> AcceptAsync(Fixture fixture, ImportConfigurationService service, string key, string value)
		{
			var change = await service.DecideAsync(DepartmentId, AdminId, ImportActorKind.Administrator, WorkspaceId, new ImportConfigurationDecision { ItemKey = key, Value = value });
			change.ApprovedRunId = "RUN";
			return change;
		}

		[Test]
		public async Task A_commit_journals_each_before_value_before_writing_it_and_marks_what_it_wrote()
		{
			var fixture = new Fixture();
			fixture.Modules.ShiftsDisabled = true;
			var service = fixture.Service();
			await AcceptAsync(fixture, service, "modules.Shifts", "true");
			await AcceptAsync(fixture, service, "terminology." + DepartmentTerms.Units, "Apparatus");
			await AcceptAsync(fixture, service, "department.use_24_hour_time", "false");
			await AcceptAsync(fixture, service, "numbering.invoice.next", "500");

			var journal = new List<ImportChangeJournalEntry>();
			var result = await service.ApplyAsync(Run(), AdminId, (entry, _) =>
			{
				fixture.Calls.Add("journal:" + entry.TargetKey);
				journal.Add(entry);
				return Task.CompletedTask;
			});

			result.Written.Should().Be(2);
			result.Unchanged.Should().Be(1, "the department already uses the 12-hour clock");
			result.Failed.Should().Be(1, "invoices still have Resgrid's built-in numbers");
			fixture.Calls.Should().ContainInOrder("journal:modules.Shifts", "write:modules", "journal:terminology." + DepartmentTerms.Units, "write:terminology");
			fixture.Modules.ShiftsDisabled.Should().BeFalse();
			fixture.Terminology.TextFor(DepartmentTerms.Units).Should().Be("Apparatus");

			journal.Should().OnlyContain(e => e.EntityType == ImportConfigurationCatalog.JournalEntityType && e.Operation == (int)ImportJournalOperation.Setting);
			journal.Single(e => e.TargetKey == "modules.Shifts").BeforeValues.Should().Be("{\"Before\":\"false\",\"After\":\"true\"}");

			fixture.Stored.Single(s => s.ItemKey == "modules.Shifts").State.Should().Be((int)ImportConfigurationChangeState.Applied);
			fixture.Stored.Single(s => s.ItemKey == "modules.Shifts").AppliedRunId.Should().Be("RUN");
			var failed = fixture.Stored.Single(s => s.ItemKey == "numbering.invoice.next");
			failed.State.Should().Be((int)ImportConfigurationChangeState.Failed);
			failed.FailureCode.Should().Be("IMP-CONFIG-NEEDS-PATTERN");

			fixture.Events.Verify(e => e.SendMessage(It.Is<AuditEvent>(a => a.Type == AuditLogTypes.DepartmentSettingsChanged && a.After.Contains("department-import"))), Times.Once);

			// A slice that resumes writes nothing twice.
			(await service.ApplyAsync(Run(), AdminId, (_, __) => { journal.Add(null); return Task.CompletedTask; })).Written.Should().Be(0);
			(await service.VerifyAsync(Run())).Should().Be((3L, 3L));
		}

		[Test]
		public async Task A_permission_is_written_on_behalf_of_the_administrator_and_refreshes_visibility()
		{
			var fixture = new Fixture();
			var service = fixture.Service();
			await AcceptAsync(fixture, service, "permission.CreateCall", ImportConfigurationCatalog.Permission((int)PermissionActions.DepartmentAndGroupAdmins, false, null));

			var result = await service.ApplyAsync(Run(), AdminId, (_, __) => Task.CompletedTask);

			result.Written.Should().Be(1);
			fixture.Permissions.Verify(p => p.SetPermissionForDepartmentAsync(DepartmentId, AdminId, PermissionTypes.CreateCall, PermissionActions.DepartmentAndGroupAdmins, null, false,
				It.IsAny<CancellationToken>()), Times.Once);
			fixture.Events.Verify(e => e.SendMessage(It.Is<AuditEvent>(a => a.Type == AuditLogTypes.PermissionsChanged)), Times.Once);
			fixture.Events.Verify(e => e.SendMessage(It.IsAny<SecurityRefreshEvent>()), Times.AtLeastOnce());
		}

		// ---- Record numbering (setting 72) -----------------------------------------------------------------------------

		[Test]
		public async Task Record_numbering_shows_only_while_Records_is_on()
		{
			var fixture = new Fixture { RecordsOn = false };
			var service = fixture.Service();

			(await service.GetAsync(DepartmentId, AdminId, WorkspaceId)).Groups.Should().NotContain(g => g.Key == ImportConfigurationGroups.Records);
			(await Refusal(() => service.DecideAsync(DepartmentId, AdminId, ImportActorKind.Administrator, WorkspaceId,
				new ImportConfigurationDecision { ItemKey = "records.pattern", Value = "{PREFIX}-{SEQ}" }))).Code.Should().Be("IMP-CONFIG-UNAVAILABLE");

			fixture.RecordsOn = true;
			fixture.RecordsIssued["RUN-" + fixture.RecordsYear + "-"] = 152;
			var view = await service.GetAsync(DepartmentId, AdminId, WorkspaceId);
			Fixture.Row(view, "records.pattern").Current.Should().Be("{PREFIX}-{YYYY}-{SEQ}", "the pattern Records has always written");
			Fixture.Row(view, "records.width").Current.Should().Be("4");
			Fixture.Row(view, "records.run.prefix").Current.Should().Be("RUN");
			Fixture.Row(view, "records.run.next").Current.Should().Be("153");
			Fixture.Row(view, "records.training.next").Current.Should().Be("1");
			Fixture.Row(view, "records.permit.pattern").Current.Should().BeEmpty("permits keep their built-in numbers");

			// A decision made while Records was on stays visible after it is turned off, so it can be undone, and holds the wave back.
			await AcceptAsync(fixture, service, "records.run.next", "400");
			fixture.RecordsOn = false;
			var off = await service.GetAsync(DepartmentId, AdminId, WorkspaceId);
			Fixture.Row(off, "records.run.next").IssueCode.Should().Be("IMP-CONFIG-UNAVAILABLE");
			(await service.GetPendingAsync(DepartmentId, WorkspaceId)).Issues.Should().ContainKey("records.run.next");
			(await service.ApplyAsync(Run(), AdminId, (_, __) => Task.CompletedTask)).Failed.Should().Be(1);
			fixture.Stored.Single().FailureCode.Should().Be("IMP-CONFIG-UNAVAILABLE");
		}

		[Test]
		public async Task Record_numbering_is_written_through_the_Records_services_in_order_and_next_numbers_only_rise()
		{
			var fixture = new Fixture();
			var service = fixture.Service();
			await AcceptAsync(fixture, service, "records.run.next", "300");
			await AcceptAsync(fixture, service, "records.run.prefix", "fire");
			await AcceptAsync(fixture, service, "records.pattern", "{prefix}{yy}-{seq}");
			await AcceptAsync(fixture, service, "records.width", "5");

			var result = await service.ApplyAsync(Run(), AdminId, (_, __) => Task.CompletedTask);

			result.Written.Should().Be(4);
			fixture.RecordNumbering.Pattern.Should().Be("{PREFIX}{YY}-{SEQ}");
			fixture.RecordNumbering.SequenceWidth.Should().Be(5);
			fixture.RecordNumbering.PrefixFor(RmsDefinitionKeys.Run).Should().Be("FIRE");
			// The next number is raised on the sequence the pattern and prefix written before it produce.
			var scope = RecordNumberFormat.Resolve("{PREFIX}{YY}-{SEQ}", 5, "FIRE", fixture.RecordsYear, null);
			fixture.RecordNumbering.FloorFor(scope.Key).Should().Be(300);
			fixture.Stored.Should().OnlyContain(s => s.State == (int)ImportConfigurationChangeState.Applied);

			// Below what the sequence already issues is refused by Records numbering itself.
			fixture.RecordsIssued[scope.Prefix] = 450;
			await AcceptAsync(fixture, service, "records.run.next", "320");
			(await service.ApplyAsync(Run(), AdminId, (_, __) => Task.CompletedTask)).Failed.Should().Be(1);
			fixture.Stored.Single(s => s.ItemKey == "records.run.next").FailureCode.Should().Be("IMP-CONFIG-BELOW-CURRENT");

			// A prefix left empty returns the type to its default.
			(await AcceptAsync(fixture, service, "records.training.prefix", " ")).Value.Should().Be("TRN");
		}

		[Test]
		public async Task Record_next_numbers_that_cannot_be_written_as_they_stand_are_named_before_approval()
		{
			var fixture = new Fixture();
			var service = fixture.Service();
			fixture.RecordsIssued["RUN-" + fixture.RecordsYear + "-"] = 152;

			await AcceptAsync(fixture, service, "records.run.next", "100");
			Fixture.Row(await service.GetAsync(DepartmentId, AdminId, WorkspaceId), "records.run.next").IssueCode.Should().Be("IMP-CONFIG-BELOW-CURRENT");

			// Without {PREFIX} every type shares one sequence: two different next numbers cannot both be written.
			await AcceptAsync(fixture, service, "records.pattern", "{YYYY}-{SEQ}");
			await AcceptAsync(fixture, service, "records.run.next", "500");
			await AcceptAsync(fixture, service, "records.training.next", "600");
			var shared = await service.GetAsync(DepartmentId, AdminId, WorkspaceId);
			Fixture.Row(shared, "records.run.next").IssueCode.Should().Be("IMP-CONFIG-RECORDS-SHARED");
			Fixture.Row(shared, "records.training.next").IssueCode.Should().Be("IMP-CONFIG-RECORDS-SHARED");

			// With {GROUP} each station has its own sequences, which the Records settings raise.
			await AcceptAsync(fixture, service, "records.pattern", "{PREFIX}-{GROUP}-{SEQ}");
			Fixture.Row(await service.GetAsync(DepartmentId, AdminId, WorkspaceId), "records.run.next").IssueCode.Should().Be("IMP-CONFIG-RECORDS-PER-GROUP");

			// A Records kind's next number belongs to its own pattern.
			await AcceptAsync(fixture, service, "records.permit.next", "40");
			Fixture.Row(await service.GetAsync(DepartmentId, AdminId, WorkspaceId), "records.permit.next").IssueCode.Should().Be("IMP-CONFIG-NEEDS-PATTERN");
		}

		[Test]
		public void Record_numbering_values_have_canonical_forms_and_department_names()
		{
			ImportConfigurationCatalog.Canonicalize(ImportConfigurationCatalog.Find("records.pattern"), "{prefix}-{yyyy}-{seq}").Should().Be(("{PREFIX}-{YYYY}-{SEQ}", (string)null));
			ImportConfigurationCatalog.Canonicalize(ImportConfigurationCatalog.Find("records.pattern"), "{PREFIX}/{SEQ}").Code.Should().Be("IMP-CONFIG-PATTERN");
			ImportConfigurationCatalog.Canonicalize(ImportConfigurationCatalog.Find("records.unit-activity.prefix"), "").Should().Be(("UNT", (string)null));
			ImportConfigurationCatalog.Canonicalize(ImportConfigurationCatalog.Find("records.neris-incident.prefix"), "nfirs").Should().Be(("NFIRS", (string)null));
			ImportConfigurationCatalog.Canonicalize(ImportConfigurationCatalog.Find("records.run.prefix"), "F").Code.Should().Be("IMP-CONFIG-PREFIX");
			ImportConfigurationCatalog.Canonicalize(ImportConfigurationCatalog.Find("records.width"), "2").Code.Should().Be("IMP-CONFIG-OUT-OF-RANGE");
			ImportConfigurationCatalog.All.Count(i => i.Group == ImportConfigurationGroups.Records).Should().Be(3 + 2 * 8 + 3 * 6);
			ImportConfigurationCatalog.All.Where(i => i.Group == ImportConfigurationGroups.Records).Should().OnlyContain(i => i.Order < ImportConfigurationCatalog.Find("permission.CreateCall").Order);

			var display = ImportConfigurationDisplay.English(null, null, ImportCatalog.Current);
			display.Group(ImportConfigurationGroups.Records).Should().Be("Record numbering");
			display.Label(ImportConfigurationCatalog.Find("records.run.next")).Should().Be("Run · next number");
			display.Label(ImportConfigurationCatalog.Find("records.permit.pattern")).Should().Be("Permits · pattern");
			display.Label(ImportConfigurationCatalog.Find("records.pattern")).Should().Be("Record numbers · pattern");
			display.Value(ImportConfigurationCatalog.Find("records.permit.next"), "").Should().Be("Resgrid's built-in numbers");
		}

		private static async Task<DepartmentImportException> Refusal(Func<Task> action) =>
			(await FluentActions.Awaiting(action).Should().ThrowAsync<DepartmentImportException>()).Which;

		private static ImportChangeJournalEntry Entry(string key, string before, string after) => new ImportChangeJournalEntry
		{
			Id = Guid.NewGuid().ToString(),
			DepartmentId = DepartmentId,
			WorkspaceId = WorkspaceId,
			RunId = "RUN",
			EntityType = ImportConfigurationCatalog.JournalEntityType,
			TargetKey = key,
			Operation = (int)ImportJournalOperation.Setting,
			BeforeValues = JsonConvert.SerializeObject(new { Before = before, After = after })
		};

		[Test]
		public async Task Rollback_restores_a_setting_nobody_changed_since_and_leaves_one_that_was()
		{
			var fixture = new Fixture();
			var service = fixture.Service();
			await AcceptAsync(fixture, service, "terminology." + DepartmentTerms.Units, "Apparatus");
			await service.ApplyAsync(Run(), AdminId, (_, __) => Task.CompletedTask);
			var rollback = new ImportRun { Id = "RB", DepartmentId = DepartmentId, WorkspaceId = WorkspaceId, Kind = (int)ImportRunKind.Rollback };

			var restored = await service.RollbackAsync(rollback, AdminId, Entry("terminology." + DepartmentTerms.Units, "", "Apparatus"));
			restored.Outcome.Should().Be(ImportRollbackOutcome.Restored);
			fixture.Terminology.TextFor(DepartmentTerms.Units).Should().BeNull("back to Resgrid's name");
			fixture.Stored.Single().State.Should().Be((int)ImportConfigurationChangeState.RolledBack);

			fixture.Terminology.Set(DepartmentTerms.Calls, "Jobs");
			var changed = await service.RollbackAsync(rollback, AdminId, Entry("terminology." + DepartmentTerms.Calls, "", "Missions"));
			changed.Outcome.Should().Be(ImportRollbackOutcome.ManualReconciliation);
			changed.Code.Should().Be("IMP-ROLLBACK-CHANGED-SINCE");
			fixture.Terminology.TextFor(DepartmentTerms.Calls).Should().Be("Jobs");

			var floor = await service.RollbackAsync(rollback, AdminId, Entry("numbering.calls.next", "40", "500"));
			floor.Outcome.Should().Be(ImportRollbackOutcome.ManualReconciliation);
			floor.Code.Should().Be("IMP-ROLLBACK-NUMBER-KEPT", "next numbers only go up");
			fixture.RecordNumbering.RaiseFloor(RecordNumberFormat.Resolve("{PREFIX}-{YYYY}-{SEQ}", 4, "RUN", fixture.RecordsYear, null).Key, 300, AdminId, DateTime.UtcNow);
			(await service.RollbackAsync(rollback, AdminId, Entry("records.run.next", "1", "300"))).Code.Should().Be("IMP-ROLLBACK-NUMBER-KEPT");

			(await service.RollbackAsync(rollback, AdminId, Entry("not.an.item", "", "x"))).Code.Should().Be("IMP-ROLLBACK-NO-WRITER");
		}

		[Test]
		public async Task An_address_without_every_part_reads_back_as_it_is_so_rollback_can_put_it_back()
		{
			var fixture = new Fixture();
			fixture.Departments.Setup(x => x.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(new Department { DepartmentId = DepartmentId, Name = "Station 1", AddressId = 12 });
			fixture.Addresses.Setup(a => a.GetAddressByIdAsync(12)).ReturnsAsync(new Address { AddressId = 12, Address1 = "1 Main St ", City = "Reno", Country = "US" });

			var current = await fixture.Targets().ReadAsync(DepartmentId, new[] { ImportConfigurationCatalog.Find("department.address") }, true);

			current["department.address"].Should().Be("{\"Address1\":\"1 Main St\",\"City\":\"Reno\",\"State\":null,\"PostalCode\":null,\"Country\":\"US\"}",
				"a legacy address without a region is still the department's address, not an empty one");
		}

		// ---- Web -----------------------------------------------------------------------------------------------------

		[Test]
		public void The_posted_parts_of_a_value_are_assembled_by_kind()
		{
			DepartmentImportController.ConfigurationValue(ImportConfigurationCatalog.Find("map.center"), new ImportConfigureInput { Latitude = "39.5", Longitude = " -119.8" })
				.Should().Be("39.5,-119.8");
			DepartmentImportController.ConfigurationValue(ImportConfigurationCatalog.Find("numbering.calls.year_start"), new ImportConfigureInput { Month = 7, Day = 1 })
				.Should().Be("07-01");
			DepartmentImportController.ConfigurationValue(ImportConfigurationCatalog.Find("permission.ViewGroupUsers"),
				new ImportConfigureInput { Action = (int)PermissionActions.DepartmentAdminsAndSelectRoles, LockToGroup = true, Roles = new List<int> { 5, 3 } })
				.Should().Be("{\"Action\":2,\"LockToGroup\":true,\"Roles\":[3,5]}");
			JsonConvert.DeserializeObject<ImportConfigurationAddress>(DepartmentImportController.ConfigurationValue(ImportConfigurationCatalog.Find("department.address"),
				new ImportConfigureInput { Address1 = "1 Main St", City = "Reno", State = "NV", Country = "US" })).City.Should().Be("Reno");
			DepartmentImportController.ConfigurationValue(ImportConfigurationCatalog.Find("terminology." + DepartmentTerms.Units), new ImportConfigureInput { Value = "Apparatus" })
				.Should().Be("Apparatus");
		}

		[Test]
		public void The_configuration_service_resolves_from_the_container()
		{
			using var scope = Bootstrapper.GetKernel().BeginLifetimeScope();
			scope.Resolve<IImportConfigurationService>().Should().BeOfType<ImportConfigurationService>();
		}
	}
}
