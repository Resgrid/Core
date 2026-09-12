using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Providers;
using Resgrid.Model.Services;
using Resgrid.Services.Records;

namespace Resgrid.Tests.Rms
{
	/// <summary>
	/// Wires the real RMS-1B/1C services (definitions, typed values, template packs, saved reports, deployments) and the real
	/// RecordsService over the in-memory fakes so behavior can be asserted end to end without a database.
	/// </summary>
	public sealed class RmsDefinitionHarness
	{
		public const int Dept = 7;
		public const string Admin = "admin";
		public const string Author = "author";

		public FakeRmsStore Store { get; } = new FakeRmsStore();
		public FakeRmsDefinitionStore Defs { get; }
		public Mock<IRecordsAuthorizationService> Authorization { get; } = new Mock<IRecordsAuthorizationService>();
		public Mock<IFeatureToggleService> Flags { get; } = new Mock<IFeatureToggleService>();
		public Mock<IDepartmentsService> Departments { get; } = new Mock<IDepartmentsService>();
		public Mock<IUnitsService> Units { get; } = new Mock<IUnitsService>();
		public Mock<IDepartmentGroupsService> Groups { get; } = new Mock<IDepartmentGroupsService>();
		public Mock<IContactsService> Contacts { get; } = new Mock<IContactsService>();
		public Mock<ICallsService> Calls { get; } = new Mock<ICallsService>();
		public Mock<IInventoryService> Inventory { get; } = new Mock<IInventoryService>();
		public Mock<IPersonnelRolesService> Roles { get; } = new Mock<IPersonnelRolesService>();
		public Mock<IRecordsCutoverService> Cutover { get; } = new Mock<IRecordsCutoverService>();
		public Mock<IDepartmentSettingsService> Settings { get; } = new Mock<IDepartmentSettingsService>();
		public Mock<IUserProfileService> Profiles { get; } = new Mock<IUserProfileService>();
		public Mock<IDepartmentDataProtectionService> Adp { get; } = new Mock<IDepartmentDataProtectionService>();
		public Mock<IRecordsEvidenceService> Evidence { get; } = new Mock<IRecordsEvidenceService>();
		public Mock<IOutboundQueueProvider> OutboundQueue { get; } = new Mock<IOutboundQueueProvider>();
		public PassthroughRecordsProtection Protection { get; } = new PassthroughRecordsProtection();
		public List<DomainEventDispatchedEvent> Published { get; } = new List<DomainEventDispatchedEvent>();

		public DomainEventOutboxService Outbox { get; }
		public RecordTemplatePacksService Templates { get; }
		public RecordTypedValuesService TypedValues { get; }
		public RecordDefinitionsService Definitions { get; }
		public RecordsService Records { get; }
		public RecordSavedReportsService Reports { get; }
		public RecordDeploymentsService Deployments { get; }
		public FakeOrderFeedProvider Feed { get; } = new FakeOrderFeedProvider();
		public Resgrid.Services.Records.Connectors.RecordDeploymentConnectorsService Connectors { get; }

		public RmsDefinitionHarness()
		{
			Resgrid.Config.SystemBehaviorConfig.CacheEnabled = false;
			Defs = new FakeRmsDefinitionStore(Store);

			Authorization.Setup(a => a.IsActiveMemberAsync(It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync(true);
			Authorization.Setup(a => a.IsDepartmentAdminAsync(It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync((string u, int d) => u == Admin && d == Dept);
			Authorization.Setup(a => a.HasPermissionAsync(It.IsAny<string>(), Dept, It.IsAny<PermissionTypes>())).ReturnsAsync(true);
			Authorization.Setup(a => a.CanUserViewRecordAsync(It.IsAny<string>(), It.IsAny<string>(), Dept)).ReturnsAsync(true);
			Authorization.Setup(a => a.CanReadSourceCallAsync(It.IsAny<string>(), Dept, It.IsAny<Call>())).ReturnsAsync(true);
			Authorization.Setup(a => a.IsGroupScopedAsync(Dept)).ReturnsAsync(false);
			Flags.Setup(f => f.IsEnabledAsync(It.IsAny<string>(), Dept, It.IsAny<bool>(), It.IsAny<IDictionary<string, string>>())).ReturnsAsync(true);

			Departments.Setup(d => d.GetAllPersonnelNamesForDepartmentAsync(Dept)).ReturnsAsync(new List<PersonName>
			{
				new PersonName { UserId = Admin, FirstName = "Ada", LastName = "Admin" }, new PersonName { UserId = Author, FirstName = "Pat", LastName = "Author" }, new PersonName { UserId = "p2", FirstName = "Sam", LastName = "Second" }
			});
			Units.Setup(u => u.GetUnitByIdAsync(5)).ReturnsAsync(new Unit { UnitId = 5, DepartmentId = Dept, Name = "Engine 5", Type = "Engine", StationGroupId = 13 });
			Units.Setup(u => u.GetUnitByIdAsync(99)).ReturnsAsync(new Unit { UnitId = 99, DepartmentId = 1, Name = "Other dept" });
			Groups.Setup(g => g.GetGroupByIdAsync(11, It.IsAny<bool>())).ReturnsAsync(new DepartmentGroup { DepartmentGroupId = 11, DepartmentId = Dept, Name = "Station 1" });
			Groups.Setup(g => g.GetGroupForUserAsync(It.IsAny<string>(), Dept)).ReturnsAsync(new DepartmentGroup { DepartmentGroupId = 11, DepartmentId = Dept, Name = "Station 1" });
			Contacts.Setup(c => c.GetContactByIdAsync("c1")).ReturnsAsync(new Contact { ContactId = "c1", DepartmentId = Dept, CompanyName = "Acme Logistics" });
			Calls.Setup(c => c.GetCallByIdAsync(77, It.IsAny<bool>())).ReturnsAsync(new Call { CallId = 77, DepartmentId = Dept, Number = "C-77", Name = "Structure fire", Type = "Fire", LoggedOn = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc) });
			Inventory.Setup(i => i.GetInventoryByIdAsync(9)).ReturnsAsync(new Inventory { InventoryId = 9, DepartmentId = Dept, Type = new InventoryType { Type = "Hose 50ft" } });
			Roles.Setup(r => r.GetRolesForUserAsync(It.IsAny<string>(), Dept)).ReturnsAsync(new List<PersonnelRole>());

			Cutover.Setup(c => c.GetModuleStateAsync(Dept, It.IsAny<bool>())).ReturnsAsync(new RecordsModuleState { DepartmentId = Dept, FlagEnabled = true, Activated = true, CutoverState = RmsDepartmentCutoverState.Active, LegacyWritesBlocked = true });
			Settings.Setup(s => s.GetRecordsNumberingConfigAsync(Dept, It.IsAny<bool>())).ReturnsAsync(new RecordsNumberingConfig());
			Settings.Setup(s => s.GetRecordsReviewDueHoursAsync(Dept, It.IsAny<bool>())).ReturnsAsync(72);
			Profiles.Setup(p => p.GetProfileByUserIdAsync(It.IsAny<string>(), It.IsAny<bool>())).ReturnsAsync((string id, bool b) => new UserProfile { UserId = id, FirstName = "First", LastName = id });
			Adp.Setup(a => a.GetPinnedCatalogVersionAsync(Dept)).ReturnsAsync(0);
			Evidence.Setup(e => e.BindToRevisionAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);
			OutboundQueue.Setup(q => q.EnqueueNotification(It.IsAny<Resgrid.Model.Queue.NotificationItem>())).ReturnsAsync(true);

			var aggregator = new Mock<IEventAggregator>();
			aggregator.Setup(a => a.SendMessage(It.IsAny<DomainEventDispatchedEvent>())).Callback<DomainEventDispatchedEvent>(e => Published.Add(e));
			// Definition, record and report events all go through the same outbox as production, guard included.
			Store.LiveContentGuard = true;
			Outbox = new DomainEventOutboxService(Store.OutboxRepo.Object, aggregator.Object);

			Templates = new RecordTemplatePacksService(Defs.PacksRepo.Object, Defs.ProfilesRepo.Object);
			TypedValues = new RecordTypedValuesService(Defs.ValuesRepo.Object, Defs.GroupsRepo.Object, Store.AttachmentsRepo.Object,
				Departments.Object, Units.Object, Groups.Object, Contacts.Object, Calls.Object, Inventory.Object, Protection);
			Definitions = new RecordDefinitionsService(Defs.DefinitionsRepo.Object, Defs.VersionsRepo.Object, Defs.SectionsRepo.Object, Defs.FieldsRepo.Object,
				Store.RecordsRepo.Object, Defs.ValuesRepo.Object, TypedValues, Templates, Authorization.Object, Protection, Outbox, Flags.Object, Store.AuditsRepo.Object, Store.UnitOfWork.Object);
			Records = new RecordsService(Store.RecordsRepo.Object, new RmsRecordValueService(Store.DetailsRepo.Object), Store.ParticipantsRepo.Object, Store.UnitsRepo.Object,
				Store.AttachmentsRepo.Object, Store.RevisionsRepo.Object, Evidence.Object, Store.ScopesRepo.Object, Store.SharesRepo.Object, Store.ProjectionsRepo.Object,
				Store.AuditsRepo.Object, Outbox, Cutover.Object, Settings.Object, Groups.Object, Profiles.Object, Units.Object, Calls.Object, Adp.Object,
				Store.UnitOfWork.Object, OutboundQueue.Object, new NullRecordAttachmentScanner(), Authorization.Object, Mock.Of<IRecordsUdfService>(), Protection, Definitions, TypedValues, Roles.Object);
			Reports = new RecordSavedReportsService(Defs.ReportsRepo.Object, Definitions, Records, Store.RecordsRepo.Object, Defs.ValuesRepo.Object, Defs.GroupsRepo.Object, Authorization.Object, Store.AuditsRepo.Object);
			Deployments = new RecordDeploymentsService(Defs.OrdersRepo.Object, Defs.FillsRepo.Object, Defs.ReferencesRepo.Object, Records, Store.RecordsRepo.Object, Definitions, Templates, Authorization.Object, Store.AuditsRepo.Object, Store.UnitOfWork.Object);
			Connectors = new Resgrid.Services.Records.Connectors.RecordDeploymentConnectorsService(Defs.ConnectorsRepo.Object, Defs.ConnectorRunsRepo.Object, Defs.OrdersRepo.Object, Defs.FillsRepo.Object,
				Deployments, Authorization.Object, Store.AuditsRepo.Object, new IExternalOrderFeedProvider[] { Feed, new FakeOrderFeedProvider(RmsExternalOrderConnectorProviders.Iroc, "iroc", RmsDeploymentProfiles.UsWildland, new Resgrid.Services.Records.Connectors.IrocOrderFeedProvider().ValidateOrder, Feed) });
		}

		/// <summary>Creates a blank department definition and replaces the starter schema with <paramref name="schema"/> in draft v1.</summary>
		public async Task<RecordDefinitionAggregate> CreateAsync(string key, string name, RecordDefinitionSchema schema, Action<RecordDefinitionDraftInput> configure = null)
		{
			var created = await Definitions.CreateAsync(Dept, Admin, new RecordDefinitionCreateInput { DefinitionKey = key, Name = name });
			var draft = created.Draft;
			var input = RecordDefinitionsService.ToDraftInput(draft);
			input.Schema = schema;
			configure?.Invoke(input);
			await Definitions.SaveDraftAsync(Dept, Admin, key, draft.Version, draft.RowVersion, input);
			return await Definitions.GetAsync(Dept, key);
		}

		public async Task<RmsRecordDefinitionVersion> PublishAsync(string key)
		{
			var aggregate = await Definitions.GetAsync(Dept, key);
			var draft = aggregate.Draft ?? throw new InvalidOperationException("no draft");
			return await Definitions.PublishAsync(Dept, Admin, key, draft.Version, draft.RowVersion);
		}

		public async Task<RmsRecordDefinitionVersion> CreateAndPublishAsync(string key, string name, RecordDefinitionSchema schema, Action<RecordDefinitionDraftInput> configure = null)
		{
			await CreateAsync(key, name, schema, configure);
			return await PublishAsync(key);
		}

		public RmsRecordDefinitionVersion Version(string key, int version) => Defs.Versions.Single(v => v.DefinitionKey == key && v.Version == version);

		public IEnumerable<DomainEventOutboxEntry> Events(WorkflowTriggerEventType trigger) => Store.Outbox.Where(e => e.TriggerEventType == (int)trigger);

		// ---- schema builders -------------------------------------------------------------------------

		public static RecordDefinitionSchema Schema(params RecordSectionSchema[] sections) => new RecordDefinitionSchema { Sections = sections.ToList() };
		public static RecordSectionSchema Section(string key, string label, params RecordFieldSchema[] fields) => new RecordSectionSchema { Key = key, Label = label, Fields = fields.ToList() };
		public static RecordSectionSchema Rows(string key, string label, int? min, int? max, params RecordFieldSchema[] fields) => new RecordSectionSchema { Key = key, Label = label, Repeating = true, MinRows = min, MaxRows = max, Fields = fields.ToList() };
		public static RecordFieldSchema Field(string key, RmsFieldType type, bool required = false, RmsFieldClassification classification = RmsFieldClassification.Standard, Action<RecordFieldSchema> configure = null)
		{
			var field = new RecordFieldSchema { Key = key, Label = char.ToUpperInvariant(key[0]) + key.Substring(1).Replace('_', ' '), Type = type, Required = required, Classification = classification };
			configure?.Invoke(field);
			return field;
		}
		public static RecordFieldSchema Select(string key, params string[] options) => Field(key, RmsFieldType.SingleSelect, configure: f => { f.Options = options.Select(o => new RecordOptionSchema { Key = o.ToLowerInvariant().Replace(' ', '-'), Label = o }).ToList(); f.Groupable = true; f.Filterable = true; f.WorkflowExposed = true; });
		public static RecordFieldSchema Multi(string key, params string[] options) => Field(key, RmsFieldType.MultiSelect, configure: f => f.Options = options.Select(o => new RecordOptionSchema { Key = o.ToLowerInvariant().Replace(' ', '-'), Label = o }).ToList());
		public static RecordRuleSchema ShowWhen(string fieldKey, string value) => new RecordRuleSchema { Effect = RmsRuleEffect.Show, Condition = new RecordConditionSchema { Operator = RmsRuleOperator.Equals, FieldKey = fieldKey, Value = value } };
		public static RecordRuleSchema RequireWhen(string fieldKey, string value) => new RecordRuleSchema { Effect = RmsRuleEffect.Require, Condition = new RecordConditionSchema { Operator = RmsRuleOperator.Equals, FieldKey = fieldKey, Value = value } };
		public static RecordValueInput Value(string section, string field, string value, string rowKey = null, int ordinal = 0) => new RecordValueInput { SectionKey = section, FieldKey = field, Value = value, RowKey = rowKey, Ordinal = ordinal };
		public static RecordValueInput Reference(string section, string field, string referenceId, string referenceType = null, string rowKey = null, int ordinal = 0) => new RecordValueInput { SectionKey = section, FieldKey = field, ReferenceId = referenceId, ReferenceType = referenceType, RowKey = rowKey, Ordinal = ordinal };

		public static RmsRecordDefinitionVersion DetachedVersion(string key, RecordDefinitionSchema schema, int version = 1) => new RmsRecordDefinitionVersion
		{
			RmsRecordDefinitionVersionId = key + "-v" + version, DepartmentId = Dept, DefinitionKey = key, Version = version, State = (int)RmsDefinitionVersionState.Published,
			LifecyclePreset = (int)RmsLifecyclePreset.QuickEntry, SchemaJson = RecordDefinitionSchema.Serialize(schema), NumberingJson = Newtonsoft.Json.JsonConvert.SerializeObject(new RecordDefinitionNumbering { Prefix = "TST" }),
			CreatedOn = DateTime.UtcNow, ModifiedOn = DateTime.UtcNow, RowVersion = 1
		};
	}
}
