using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Config;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services.Records;

namespace Resgrid.Tests.Rms
{
	/// <summary>
	/// FieldRecordCatalogV1 matrix (RMS plan RMS-1D): parent and four child flags, app/client type and minimum
	/// version, membership and authoring permission, verified context, Protected Data state, definition retirement
	/// and unsupported controls. The governing rule under test is that a forged client, app, capability or context
	/// value never widens the returned catalog.
	/// </summary>
	[TestFixture]
	public class FieldRecordCatalogTests
	{
		private const int Dept = 9;
		private const string Me = "responder";

		private Mock<IRecordsCutoverService> _cutover;
		private Mock<IRecordsAuthorizationService> _authorization;
		private Mock<IFeatureToggleService> _flags;
		private Mock<IRecordDefinitionsService> _definitions;
		private Mock<IDepartmentDataProtectionService> _protection;
		private Mock<IRecordsService> _records;
		private Mock<IRecordWorkAssignmentsService> _assignments;
		private Mock<IUnitsService> _units;
		private Mock<IDepartmentGroupsService> _groups;
		private Mock<ICallsService> _calls;
		private Mock<IIncidentCommandService> _command;
		private RecordsModuleState _moduleState;
		private DepartmentDataProtectionPolicy _policy;
		private List<RmsRecordDefinitionVersion> _published;
		private List<RecordDefinitionSummary> _summaries;
		private FieldRecordsService _service;
		private string _minimumResponder;

		[SetUp]
		public void SetUp()
		{
			_minimumResponder = RecordsFieldConfig.MinimumResponderVersion;
			_moduleState = new RecordsModuleState { DepartmentId = Dept, FlagEnabled = true, Activated = true, CutoverState = RmsDepartmentCutoverState.Active };
			_cutover = new Mock<IRecordsCutoverService>();
			_cutover.Setup(c => c.GetModuleStateAsync(Dept, It.IsAny<bool>())).ReturnsAsync(() => _moduleState);

			_authorization = new Mock<IRecordsAuthorizationService>();
			_authorization.Setup(a => a.IsActiveMemberAsync(It.IsAny<string>(), Dept)).ReturnsAsync(true);
			_authorization.Setup(a => a.HasPermissionAsync(It.IsAny<string>(), Dept, It.IsAny<PermissionTypes>())).ReturnsAsync(true);
			_authorization.Setup(a => a.GetReadScopeStampAsync(It.IsAny<string>(), Dept)).ReturnsAsync("scope-1");
			_authorization.Setup(a => a.GetVisibleGroupIdsAsync(It.IsAny<string>(), Dept)).ReturnsAsync((List<int>)null);
			_authorization.Setup(a => a.CanUserViewRecordAsync(It.IsAny<string>(), It.IsAny<string>(), Dept)).ReturnsAsync(true);
			_authorization.Setup(a => a.CanReadSourceCallAsync(It.IsAny<string>(), Dept, It.IsAny<Call>())).ReturnsAsync(true);

			_flags = new Mock<IFeatureToggleService>();
			_flags.Setup(f => f.IsEnabledAsync(It.IsAny<string>(), Dept, It.IsAny<bool>(), It.IsAny<IDictionary<string, string>>())).ReturnsAsync(true);

			_published = new List<RmsRecordDefinitionVersion>();
			_summaries = new List<RecordDefinitionSummary>();
			_definitions = new Mock<IRecordDefinitionsService>();
			_definitions.Setup(d => d.GetPublishedAsync(Dept)).ReturnsAsync(() => _published);
			_definitions.Setup(d => d.ListAsync(Dept, It.IsAny<bool>())).ReturnsAsync(() => _summaries);
			_definitions.Setup(d => d.GetVersionAsync(Dept, It.IsAny<string>(), It.IsAny<int>()))
				.ReturnsAsync((int dept, string key, int version) => _published.FirstOrDefault(v => v.DefinitionKey == key && v.Version == version));

			_policy = null;
			_protection = new Mock<IDepartmentDataProtectionService>();
			_protection.Setup(p => p.GetPolicyByDepartmentIdAsync(Dept, It.IsAny<bool>())).ReturnsAsync(() => _policy);

			_records = new Mock<IRecordsService>();
			_records.Setup(r => r.GetChangesSinceAsync(Dept, It.IsAny<DateTime?>(), It.IsAny<int>(), It.IsAny<string>())).ReturnsAsync(new List<RmsRecordSearchProjection>());
			_records.Setup(r => r.QueryAsync(Dept, It.IsAny<RmsRecordQuery>())).ReturnsAsync(new List<RmsRecordSearchProjection>());
			_assignments = new Mock<IRecordWorkAssignmentsService>();
			_assignments.Setup(a => a.GetQueueAsync(Dept, It.IsAny<string>(), It.IsAny<FieldRecordContext>(), It.IsAny<int>())).ReturnsAsync(new List<RmsRecordWorkAssignment>());

			_units = new Mock<IUnitsService>();
			_groups = new Mock<IDepartmentGroupsService>();
			_calls = new Mock<ICallsService>();
			_command = new Mock<IIncidentCommandService>();

			_service = new FieldRecordsService(_cutover.Object, _authorization.Object, _flags.Object, _definitions.Object, _protection.Object, _records.Object,
				_assignments.Object, _units.Object, _groups.Object, _calls.Object, _command.Object, Mock.Of<IRecordsFieldRolloutService>());
		}

		[TearDown]
		public void TearDown() => RecordsFieldConfig.MinimumResponderVersion = _minimumResponder;

		private static FieldRecordCatalogRequest Request(RmsOriginClient origin = RmsOriginClient.Responder, string capability = RecordsClientCapabilities.Packs, string appVersion = "5.2.0", FieldRecordContext context = null)
			=> new FieldRecordCatalogRequest { Origin = origin, ClientCapability = capability, AppVersion = appVersion, Context = context ?? new FieldRecordContext() };

		private RmsRecordDefinitionVersion Publish(string key, string name, Action<RecordDefinitionClientSurface> surface = null, Action<RecordDefinitionSchema> schema = null, bool retired = false)
		{
			var definitionSchema = RmsDefinitionHarness.Schema(RmsDefinitionHarness.Section("main", "Main", RmsDefinitionHarness.Field("summary", RmsFieldType.ShortText)));
			schema?.Invoke(definitionSchema);
			var clientSurface = new RecordDefinitionClientSurface { Responder = true, AllowOffline = true, AllowAttachments = true, LaunchContexts = { FieldRecordCatalogV1.LaunchContexts.None } };
			surface?.Invoke(clientSurface);
			var version = new RmsRecordDefinitionVersion
			{
				DepartmentId = Dept, DefinitionKey = key, Version = 3, State = (int)RmsDefinitionVersionState.Published, LifecyclePreset = (int)RmsLifecyclePreset.QuickEntry,
				Schema = definitionSchema, ClientSurface = clientSurface, SchemaChecksum = "chk-" + key, MinimumClientCapability = RecordsClientCapabilities.Derive(definitionSchema)
			};
			_published.Add(version);
			_summaries.Add(new RecordDefinitionSummary { Key = key, Name = name, Category = "Operations", PublishedVersion = 3, Retired = retired });
			return version;
		}

		[Test]
		public async Task Parent_and_child_flags_membership_and_version_each_fail_the_preflight_closed()
		{
			(await _service.PreflightAsync(Dept, Me, RmsOriginClient.Responder, "5.2.0", RecordsClientCapabilities.Packs)).Ok.Should().BeTrue();

			(await _service.PreflightAsync(Dept, Me, RmsOriginClient.Web, "5.2.0", null)).Reasons.Should().Contain(FieldRecordCatalogV1.ExclusionReasons.OriginNotField);

			_moduleState.FlagEnabled = false;
			var moduleOff = await _service.PreflightAsync(Dept, Me, RmsOriginClient.Responder, "5.2.0", null);
			moduleOff.Ok.Should().BeFalse();
			moduleOff.Reasons.Should().Contain(FieldRecordCatalogV1.ExclusionReasons.ModuleDisabled);
			_moduleState.FlagEnabled = true;

			_moduleState.Activated = false;
			(await _service.PreflightAsync(Dept, Me, RmsOriginClient.Responder, "5.2.0", null)).Reasons.Should().Contain(FieldRecordCatalogV1.ExclusionReasons.RecordsNotUsable);
			_moduleState.Activated = true;

			_flags.Setup(f => f.IsEnabledAsync(FeatureFlagKeys.RecordsFieldResponder, Dept, It.IsAny<bool>(), It.IsAny<IDictionary<string, string>>())).ReturnsAsync(false);
			(await _service.PreflightAsync(Dept, Me, RmsOriginClient.Responder, "5.2.0", null)).Reasons.Should().Contain(FieldRecordCatalogV1.ExclusionReasons.AppDisabled);
			(await _service.PreflightAsync(Dept, Me, RmsOriginClient.Unit, "5.2.0", null)).Ok.Should().BeTrue("each app has its own child flag");
			_flags.Setup(f => f.IsEnabledAsync(FeatureFlagKeys.RecordsFieldResponder, Dept, It.IsAny<bool>(), It.IsAny<IDictionary<string, string>>())).ReturnsAsync(true);

			_authorization.Setup(a => a.IsActiveMemberAsync(Me, Dept)).ReturnsAsync(false);
			(await _service.PreflightAsync(Dept, Me, RmsOriginClient.Responder, "5.2.0", null)).Reasons.Should().Contain(FieldRecordCatalogV1.ExclusionReasons.NotMember);
			_authorization.Setup(a => a.IsActiveMemberAsync(Me, Dept)).ReturnsAsync(true);

			RecordsFieldConfig.MinimumResponderVersion = "5.3.0";
			(await _service.PreflightAsync(Dept, Me, RmsOriginClient.Responder, "5.2.9", null)).Reasons.Should().Contain(FieldRecordCatalogV1.ExclusionReasons.AppVersionTooOld);
			(await _service.PreflightAsync(Dept, Me, RmsOriginClient.Responder, "5.10.0", null)).Ok.Should().BeTrue("versions compare numerically, not as strings");
			(await _service.PreflightAsync(Dept, Me, RmsOriginClient.Responder, null, null)).Reasons.Should().Contain(FieldRecordCatalogV1.ExclusionReasons.AppVersionTooOld, "an unreported version never satisfies a minimum");
		}

		[Test]
		public async Task Catalog_lists_the_locked_starters_for_each_app_and_only_in_their_launch_contexts()
		{
			var home = await _service.GetCatalogAsync(Dept, Me, Request());
			home.Ok.Should().BeTrue();
			home.Definitions.Where(d => d.Locked).Select(d => d.DefinitionKey).Should().BeEquivalentTo(FieldRecordCatalogV1.LockedStarterAllowlist(RmsOriginClient.Responder));
			home.Definitions.Should().NotContain(d => d.DefinitionKey == RmsDefinitionKeys.Run, "the run report needs a Call context");

			var call = new Call { CallId = 501, DepartmentId = Dept, Number = "2026-0501" };
			_calls.Setup(c => c.GetCallByIdAsync(501, It.IsAny<bool>())).ReturnsAsync(call);
			var onCall = await _service.GetCatalogAsync(Dept, "dispatcher", Request(RmsOriginClient.Dispatch, context: new FieldRecordContext { CallId = 501 }));
			onCall.Definitions.Select(d => d.DefinitionKey).Should().Contain(RmsDefinitionKeys.Run).And.Contain(RmsDefinitionKeys.Callback);
			onCall.ContextVerified.Should().BeTrue();

			var dispatchHome = await _service.GetCatalogAsync(Dept, "dispatcher", Request(RmsOriginClient.Dispatch));
			dispatchHome.Definitions.Should().NotContain(d => d.DefinitionKey == RmsDefinitionKeys.Run);
			dispatchHome.Exclusions.Should().Contain(e => e.DefinitionKey == RmsDefinitionKeys.Run && e.Reason == FieldRecordCatalogV1.ExclusionReasons.ContextNotAllowed,
				"a definition withheld for context carries a coded reason");

			var starved = await _service.GetCatalogAsync(Dept, Me, Request(capability: "records.v0"));
			starved.Definitions.Should().NotBeEmpty("an unknown capability degrades to the oldest, which still renders locked definitions");
			starved.Definitions.Should().OnlyContain(d => d.Locked);
		}

		[Test]
		public async Task Department_definitions_reach_an_app_only_through_their_client_surface_context_version_and_capability()
		{
			Publish("shift-log", "Shift log");
			Publish("unit-only", "Unit only", s => { s.Responder = false; s.Unit = true; });
			Publish("call-only", "Call only", s => { s.LaunchContexts.Clear(); s.LaunchContexts.Add(FieldRecordCatalogV1.LaunchContexts.Call); });
			Publish("needs-newer-app", "Needs newer app", s => s.MinimumAppVersion = "9.0.0");
			Publish("retired-one", "Retired", retired: true);
			Publish("packs", "Pack fields", schema: sch => sch.Sections[0].Fields.Add(RmsDefinitionHarness.Field("cost", RmsFieldType.Currency)));

			var catalog = await _service.GetCatalogAsync(Dept, Me, Request(capability: RecordsClientCapabilities.Configurable));

			catalog.Definitions.Where(d => !d.Locked).Select(d => d.DefinitionKey).Should().BeEquivalentTo(new[] { "shift-log" });
			var reasons = catalog.Exclusions.ToDictionary(e => e.DefinitionKey, e => e.Reason, StringComparer.OrdinalIgnoreCase);
			reasons["unit-only"].Should().Be(FieldRecordCatalogV1.ExclusionReasons.SurfaceNotEnabled);
			reasons["call-only"].Should().Be(FieldRecordCatalogV1.ExclusionReasons.ContextNotAllowed);
			reasons["needs-newer-app"].Should().Be(FieldRecordCatalogV1.ExclusionReasons.AppVersionTooOld);
			reasons["retired-one"].Should().Be(FieldRecordCatalogV1.ExclusionReasons.Retired);
			reasons["packs"].Should().Be(FieldRecordCatalogV1.ExclusionReasons.CapabilityUnsupported, "a client that cannot render a control is refused rather than sent one");

			var newer = await _service.GetCatalogAsync(Dept, Me, Request(capability: RecordsClientCapabilities.Packs));
			newer.Definitions.Select(d => d.DefinitionKey).Should().Contain("packs");
		}

		[Test]
		public async Task Protected_definitions_need_an_enrolled_department_and_never_go_offline()
		{
			Publish("casualty", "Casualty", schema: sch => sch.Sections[0].Fields.Add(RmsDefinitionHarness.Field("condition", RmsFieldType.LongText, classification: RmsFieldClassification.Protected)));

			var disabled = await _service.GetCatalogAsync(Dept, Me, Request());
			disabled.Exclusions.Should().Contain(e => e.DefinitionKey == "casualty" && e.Reason == FieldRecordCatalogV1.ExclusionReasons.ProtectedDataUnavailable);

			_policy = new DepartmentDataProtectionPolicy { DepartmentId = Dept, State = (int)DepartmentDataProtectionState.Enabled, CatalogVersion = 11 };
			var enrolled = await _service.GetCatalogAsync(Dept, Me, Request());
			var entry = enrolled.Definitions.Single(d => d.DefinitionKey == "casualty");
			entry.RequiresProtectedGrant.Should().BeTrue();
			entry.AllowOffline.Should().BeFalse("a sealed value never sits in an offline draft, whatever the surface asked for");
			entry.Restricted.Should().BeTrue();
			enrolled.ProtectionState.Should().Be(DepartmentDataProtectionState.Enabled.ToString());
		}

		[Test]
		public async Task A_forged_context_is_verified_server_side_and_never_widens_the_catalog()
		{
			Publish("crew-log", "Crew log", s => { s.Responder = false; s.Unit = true; s.LaunchContexts.Clear(); s.LaunchContexts.Add(FieldRecordCatalogV1.LaunchContexts.Unit); });
			_units.Setup(u => u.GetUnitByIdAsync(7)).ReturnsAsync(new Unit { UnitId = 7, DepartmentId = Dept, Name = "Engine 7" });
			_units.Setup(u => u.GetLastUnitStateByUnitIdAsync(7)).ReturnsAsync(new UnitState { UnitStateId = 1, UnitId = 7, Roles = new List<UnitStateRole>() });

			var unstaffed = await _service.GetCatalogAsync(Dept, "crew", Request(RmsOriginClient.Unit, context: new FieldRecordContext { UnitId = 7 }));
			unstaffed.Ok.Should().BeFalse("the Unit app authors only on the apparatus the caller is staffed on");
			unstaffed.Reasons.Should().Contain(FieldRecordCatalogV1.ExclusionReasons.ContextNotVerified);
			unstaffed.Definitions.Should().BeEmpty();

			_units.Setup(u => u.GetLastUnitStateByUnitIdAsync(7)).ReturnsAsync(new UnitState { UnitStateId = 2, UnitId = 7, Roles = new List<UnitStateRole> { new UnitStateRole { UserId = "crew", Role = "Driver" } } });
			var staffed = await _service.GetCatalogAsync(Dept, "crew", Request(RmsOriginClient.Unit, context: new FieldRecordContext { UnitId = 7 }));
			staffed.Ok.Should().BeTrue();
			staffed.Definitions.Select(d => d.DefinitionKey).Should().Contain("crew-log");

			_units.Setup(u => u.GetUnitByIdAsync(99)).ReturnsAsync(new Unit { UnitId = 99, DepartmentId = Dept + 1, Name = "Foreign" });
			var foreign = await _service.GetCatalogAsync(Dept, "crew", Request(RmsOriginClient.Unit, context: new FieldRecordContext { UnitId = 99 }));
			foreign.Ok.Should().BeFalse("a unit in another department is not a context");

			var claimedCommand = await _service.GetCatalogAsync(Dept, "crew", Request(RmsOriginClient.IncidentCommand, context: new FieldRecordContext { CallId = 501, CommandRole = "Operations" }));
			claimedCommand.Ok.Should().BeFalse("a command role is checked against the active command, never taken from the client");
			claimedCommand.Reasons.Should().Contain(FieldRecordCatalogV1.ExclusionReasons.ContextNotVerified);
		}

		[Test]
		public async Task Prefill_is_server_calculated_provenance_stamped_and_refused_outside_the_callers_catalog()
		{
			Publish("run-sheet", "Run sheet", s => { s.LaunchContexts.Clear(); s.LaunchContexts.Add(FieldRecordCatalogV1.LaunchContexts.Call); }, sch =>
			{
				sch.Sections[0].Fields.Add(RmsDefinitionHarness.Field("related_call", RmsFieldType.CallReference));
				sch.Sections[0].Fields.Add(RmsDefinitionHarness.Field("scene_location", RmsFieldType.Address));
				sch.Sections[0].Fields.Add(RmsDefinitionHarness.Field("started_at", RmsFieldType.DateTime));
				sch.Sections[0].Fields.Add(RmsDefinitionHarness.Field("reported_by", RmsFieldType.Person));
				sch.Sections[0].Fields.Add(RmsDefinitionHarness.Field("patient_location", RmsFieldType.Address, classification: RmsFieldClassification.Restricted));
			});
			var call = new Call { CallId = 501, DepartmentId = Dept, Number = "2026-0501", Address = "12 Pine St", LoggedOn = new DateTime(2026, 9, 6, 3, 15, 0, DateTimeKind.Utc) };
			_calls.Setup(c => c.GetCallByIdAsync(501, It.IsAny<bool>())).ReturnsAsync(call);
			_groups.Setup(g => g.GetGroupForUserAsync(Me, Dept)).ReturnsAsync(new DepartmentGroup { DepartmentGroupId = 4, DepartmentId = Dept, Name = "Station 1" });
			var request = Request(context: new FieldRecordContext { CallId = 501 });

			var prefill = await _service.PrefillAsync(Dept, Me, request, "run-sheet", 3);

			prefill.CallId.Should().Be(501);
			prefill.StationGroupId.Should().Be(4);
			prefill.SuggestedParticipantUserIds.Should().Contain(Me);
			var values = prefill.Values.ToDictionary(v => v.FieldKey, v => v.Value);
			values["related_call"].Should().Be("501");
			values["scene_location"].Should().Be("12 Pine St");
			values["started_at"].Should().Be(call.LoggedOn.ToString("O"));
			values["reported_by"].Should().Be(Me);
			values.Should().NotContainKey("patient_location", "prefill is minimum-necessary: never a restricted or protected value");
			prefill.Provenance.Should().Contain(p => p.FieldKey == "scene_location" && p.Source == "call.address" && p.SourceId == "501");

			Func<Task> outsideCatalog = () => _service.PrefillAsync(Dept, Me, Request(), "run-sheet", 3);
			await outsideCatalog.Should().ThrowAsync<UnauthorizedAccessException>("prefill answers only for an entry in the same request's catalog");

			Func<Task> unknown = () => _service.PrefillAsync(Dept, Me, request, "run-sheet", 99);
			await unknown.Should().ThrowAsync<UnauthorizedAccessException>();
		}

		[Test]
		public async Task Sync_is_bounded_tombstones_what_the_caller_cannot_read_and_resets_on_a_scope_change()
		{
			Publish("shift-log", "Shift log");
			var visible = new RmsRecordSearchProjection { RmsRecordSearchProjectionId = "r1", DepartmentId = Dept, ModifiedOn = new DateTime(2026, 9, 6, 1, 0, 0, DateTimeKind.Utc), State = (int)RmsRecordState.Finalized };
			var hidden = new RmsRecordSearchProjection { RmsRecordSearchProjectionId = "r2", DepartmentId = Dept, ModifiedOn = new DateTime(2026, 9, 6, 2, 0, 0, DateTimeKind.Utc), State = (int)RmsRecordState.Finalized };
			var deleted = new RmsRecordSearchProjection { RmsRecordSearchProjectionId = "r3", DepartmentId = Dept, ModifiedOn = new DateTime(2026, 9, 6, 2, 5, 0, DateTimeKind.Utc), DeletedOn = DateTime.UtcNow };
			_records.Setup(r => r.GetChangesSinceAsync(Dept, It.IsAny<DateTime?>(), It.IsAny<int>(), It.IsAny<string>())).ReturnsAsync(new List<RmsRecordSearchProjection> { visible, hidden, deleted });
			_authorization.Setup(a => a.CanUserViewRecordAsync(Me, "r2", Dept)).ReturnsAsync(false);
			_records.Setup(r => r.QueryAsync(Dept, It.IsAny<RmsRecordQuery>())).ReturnsAsync(new List<RmsRecordSearchProjection> { visible });
			_assignments.Setup(a => a.GetQueueAsync(Dept, Me, It.IsAny<FieldRecordContext>(), It.IsAny<int>()))
				.ReturnsAsync(new List<RmsRecordWorkAssignment> { new RmsRecordWorkAssignment { RmsRecordWorkAssignmentId = "a1", RecordId = "r1", State = (int)RmsWorkAssignmentState.Open } });

			var bundle = await _service.SyncAsync(Dept, Me, new FieldRecordSyncRequest { Origin = RmsOriginClient.Responder, AppVersion = "5.2.0", ClientCapability = RecordsClientCapabilities.Packs, Take = 500 });

			bundle.Ok.Should().BeTrue();
			bundle.ScopeStamp.Should().Be("scope-1");
			bundle.Changes.Select(c => c.RmsRecordSearchProjectionId).Should().Equal("r1");
			bundle.Tombstones.Should().BeEquivalentTo("r2", "r3");
			bundle.Drafts.Should().ContainSingle();
			bundle.Assignments.Should().ContainSingle();
			bundle.Catalog.Should().NotBeNull();
			_records.Verify(r => r.GetChangesSinceAsync(Dept, It.IsAny<DateTime?>(), RecordsFieldConfig.SyncTakeMax + 1, It.IsAny<string>()), Times.Once, "a field bundle is a working set, not an archive pull");

			var stale = await _service.SyncAsync(Dept, Me, new FieldRecordSyncRequest { Origin = RmsOriginClient.Responder, AppVersion = "5.2.0", Since = 1_700_000_000_000, ScopeStamp = "scope-0" });
			stale.ResetRequired.Should().BeTrue();
			stale.Changes.Should().BeEmpty();

			_flags.Setup(f => f.IsEnabledAsync(FeatureFlagKeys.RecordsFieldResponder, Dept, It.IsAny<bool>(), It.IsAny<IDictionary<string, string>>())).ReturnsAsync(false);
			var gated = await _service.SyncAsync(Dept, Me, new FieldRecordSyncRequest { Origin = RmsOriginClient.Responder, AppVersion = "5.2.0", Since = 1_700_000_000_000, ScopeStamp = "scope-1" });
			gated.Ok.Should().BeFalse();
			gated.Reasons.Should().Contain(FieldRecordCatalogV1.ExclusionReasons.AppDisabled);
			gated.Changes.Should().BeEmpty();
		}

		[Test]
		public void Version_comparison_is_numeric_and_tolerates_prereleases()
		{
			FieldRecordCatalogV1.CompareVersions("1.2.10", "1.2.9").Should().BePositive();
			FieldRecordCatalogV1.CompareVersions("2.0", "2.0.0").Should().Be(0);
			FieldRecordCatalogV1.CompareVersions("v5.1.0-beta.3", "5.1.0").Should().Be(0, "a prerelease suffix is not a version segment");
			FieldRecordCatalogV1.MeetsMinimum("5.0.0", null).Should().BeTrue();
			FieldRecordCatalogV1.MeetsMinimum(null, "5.0.0").Should().BeFalse();
			FieldRecordCatalogV1.IsFieldOrigin(RmsOriginClient.Web).Should().BeFalse();
			FieldRecordCatalogV1.IsFieldOrigin(RmsOriginClient.Unit).Should().BeTrue();
		}
	}
}
