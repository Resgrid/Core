using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Newtonsoft.Json;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Model.Identity;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services.ProtectedWorkflows
{
	/// <summary>
	/// In-memory Protected Workflows world: repositories backed by lists (reads return copies, the way a database
	/// would), a fake broker, a capturing executor, and the REAL WorkflowService, ProtectedWorkflowService,
	/// ProtectedWorkflowRuntime and WorkflowTemplateContextBuilder wired together.
	/// </summary>
	internal sealed class ProtectedWorkflowHarness
	{
		public const int DepartmentId = 42;
		public const string DepartmentCode = "ABCD";
		public const string AdminA = "admin-a";
		public const string AdminB = "admin-b";
		public const string Member = "member-1";
		public const string CredentialId = "cred-1";
		public const string Host = "org.crm.dynamics.com";
		public const string Url = "https://org.crm.dynamics.com/api/data/v9.2/incidents(00000000-0000-0000-0000-000000000001)";
		public const int CallId = 1001;

		// Distinctive plaintext: it must never appear in anything the platform writes.
		public const string SentinelCompletedNotes = "SENTINEL-COMPLETED-7f3a91";
		public const string SentinelFormOutcome = "SENTINEL-FORM-OUTCOME-2c8e44";
		public const string SentinelNotes = "SENTINEL-CALL-NOTES-b61d02";

		public const string EnvelopeCompletedNotes = "rgdp:1:1:Q09NUExFVEVETk9URVM=";
		public const string EnvelopeForm = "rgdp:1:1:Rk9STURBVEE=";
		public const string EnvelopeNotes = "rgdp:1:1:Tk9URVM=";
		public const string EnvelopeNature = "rgdp:1:1:TkFUVVJF";

		public Workflow Workflow { get; }

		/// <summary>Further workflows in the same department (several workflows may share a trigger).</summary>
		public List<Workflow> OtherWorkflows { get; } = new List<Workflow>();
		public List<WorkflowStep> Steps { get; } = new List<WorkflowStep>();
		public List<WorkflowCredential> Credentials { get; } = new List<WorkflowCredential>();
		public List<WorkflowProtectedRelease> Releases { get; } = new List<WorkflowProtectedRelease>();
		public List<ProtectedWorkflowDisclosure> Disclosures { get; } = new List<ProtectedWorkflowDisclosure>();
		public Dictionary<string, WorkflowRun> Runs { get; } = new Dictionary<string, WorkflowRun>();
		public List<WorkflowRunLog> Logs { get; } = new List<WorkflowRunLog>();
		public Dictionary<int, Call> Calls { get; } = new Dictionary<int, Call>();

		/// <summary>The department's active call custom field definition (enabled fields) and the stored values.</summary>
		public List<UdfField> CustomFields { get; } = new List<UdfField>();
		public List<UdfFieldValue> CustomValues { get; } = new List<UdfFieldValue>();
		public const string CustomDefinitionId = "udf-def-1";

		/// <summary>Conditional subject-identifier writes that lost to a concurrent edit (test seam: set to force losses).</summary>
		public int SubjectIdentifierWriteConflicts { get; set; }
		public bool FailDisclosureAppends { get; set; }

		/// <summary>The release store is unreachable: the run gate cannot tell whether the workflow is protected.</summary>
		public bool FailReleaseLookups { get; set; }

		/// <summary>Runs inside a conditional release write, before the version check: a concurrent writer racing it.</summary>
		public Action<WorkflowProtectedRelease> BeforeReleaseUpdate { get; set; }
		public DepartmentDataProtectionPolicy Policy { get; }
		public DepartmentProtectedDataEgressPolicy Egress { get; set; }
		public int EpochBumps { get; private set; }
		public List<string> Notifications { get; } = new List<string>();

		public FakeBroker Broker { get; } = new FakeBroker();
		public IWorkflowActionExecutor Executor { get; set; }
		public CapturingExecutor Capturing { get; } = new CapturingExecutor();

		public ProtectedWorkflowService Service { get; }
		public ProtectedWorkflowRuntime Runtime { get; }
		public WorkflowService WorkflowService { get; }

		private readonly ProtectedProjectionService _projection;

		public ProtectedWorkflowHarness(int triggerEventType = (int)WorkflowTriggerEventType.CallClosed)
		{
			Executor = Capturing;
			Workflow = new Workflow
			{
				WorkflowId = "wf-1",
				DepartmentId = DepartmentId,
				Name = "DMH case write-back",
				TriggerEventType = triggerEventType,
				IsEnabled = true,
				MaxRetryCount = 3,
				CreatedByUserId = AdminA,
				CreatedOn = DateTime.UtcNow.AddDays(-10)
			};

			Steps.Add(new WorkflowStep
			{
				WorkflowStepId = "step-1",
				WorkflowId = Workflow.WorkflowId,
				ActionType = (int)WorkflowActionType.CallApiPut,
				StepOrder = 1,
				IsEnabled = true,
				WorkflowCredentialId = CredentialId,
				ActionConfig = JsonConvert.SerializeObject(new { Url, ContentType = "application/json" }),
				OutputTemplate = "{\"closure\":\"{{ protected.call.completed_notes }}\",\"outcome\":\"{{ protected.call.form.outcome }}\",\"nature\":\"{{ call.nature }}\",\"number\":\"{{ call.number }}\"}",
				CreatedByUserId = AdminA,
				CreatedOn = DateTime.UtcNow.AddDays(-10)
			});

			Credentials.Add(new WorkflowCredential
			{
				WorkflowCredentialId = CredentialId,
				DepartmentId = DepartmentId,
				Name = "Dataverse",
				CredentialType = (int)WorkflowCredentialType.HttpBearer,
				EncryptedData = "enc:" + JsonConvert.SerializeObject(new { token = "token-abc" }),
				CreatedByUserId = AdminA
			});

			Policy = new DepartmentDataProtectionPolicy
			{
				DepartmentId = DepartmentId,
				State = (int)DepartmentDataProtectionState.Enabled,
				CatalogVersion = 29,
				PolicyEpoch = 7
			};

			Egress = new DepartmentProtectedDataEgressPolicy
			{
				DepartmentProtectedDataEgressPolicyId = 1,
				DepartmentId = DepartmentId,
				ProtectedWorkflowsEnabled = true,
				ProtectedWorkflowsAckVersion = ProtectedWorkflowDefaults.WarningTextVersion,
				ProtectedWorkflowsAckByUserId = AdminA,
				ProtectedWorkflowsAckOn = DateTime.UtcNow.AddDays(-1)
			};

			Broker.Plaintext[EnvelopeCompletedNotes] = SentinelCompletedNotes;
			// Call form data is the form builder's field array with each answer in userData.
			Broker.Plaintext[EnvelopeForm] = "[{\"type\":\"text\",\"label\":\"Outcome\",\"name\":\"outcome\",\"userData\":[\"" + SentinelFormOutcome +
				"\"]},{\"type\":\"select\",\"label\":\"Follow up\",\"name\":\"follow_up\",\"userData\":[\"yes\"]}]";
			Calls[CallId] = BuildCall();
			Broker.Plaintext[EnvelopeNotes] = SentinelNotes;
			Broker.Plaintext[EnvelopeNature] = "Behavioral health crisis";

			var releases = ReleaseRepository();
			var disclosures = DisclosureRepository();
			var workflows = WorkflowRepository();
			var steps = StepRepository();
			var credentials = CredentialRepository();
			var dataProtection = DataProtection();
			var departments = Departments();

			var permissions = new Mock<IPermissionsService>();
			permissions.Setup(p => p.GetPermissionByDepartmentTypeAsync(It.IsAny<int>(), It.IsAny<PermissionTypes>())).ReturnsAsync((Permission)null);
			permissions.Setup(p => p.IsUserAllowed(It.IsAny<Permission>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<List<PersonnelRole>>()))
				.Returns((Permission permission, bool isAdmin, bool isGroupAdmin, List<PersonnelRole> roles) =>
					permission.Action == (int)PermissionActions.Everyone ||
					(permission.Action == (int)PermissionActions.DepartmentAdminsOnly && isAdmin) ||
					(permission.Action == (int)PermissionActions.DepartmentAndGroupAdmins && (isAdmin || isGroupAdmin)));

			var groups = new Mock<IDepartmentGroupsService>();
			groups.Setup(g => g.GetGroupForUserAsync(It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync((DepartmentGroup)null);
			var roles = new Mock<IPersonnelRolesService>();
			roles.Setup(r => r.GetRolesForUserAsync(It.IsAny<string>(), It.IsAny<int>())).ReturnsAsync(new List<PersonnelRole>());

			var encryption = new Mock<IEncryptionService>();
			encryption.Setup(e => e.EncryptForDepartment(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>())).Returns((string text, int d, string c) => "enc:" + text);
			encryption.Setup(e => e.DecryptForDepartment(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()))
				.Returns((string text, int d, string c) => text != null && text.StartsWith("enc:") ? text.Substring(4) : text);

			var communication = new Mock<ICommunicationService>();
			communication.Setup(c => c.SendNotificationAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(),
					It.IsAny<Department>(), It.IsAny<string>(), It.IsAny<UserProfile>(), It.IsAny<bool>()))
				.Callback((string user, int dept, string message, string number, Department department, string title, UserProfile profile, bool ic) =>
					Notifications.Add($"{user}|{title}|{message}"))
				.ReturnsAsync(true);

			var udfDefinitions = new Mock<IUdfDefinitionRepository>();
			udfDefinitions.Setup(r => r.GetActiveDefinitionByDepartmentAndEntityTypeAsync(It.IsAny<int>(), It.IsAny<int>()))
				.ReturnsAsync((int d, int t) => t == (int)UdfEntityType.Call && CustomFields.Count > 0
					? new UdfDefinition { UdfDefinitionId = CustomDefinitionId, DepartmentId = d, EntityType = t, IsActive = true }
					: null);
			var udfFields = new Mock<IUdfFieldRepository>();
			udfFields.Setup(r => r.GetFieldsByDefinitionIdAsync(It.IsAny<string>()))
				.ReturnsAsync((string id) => id == CustomDefinitionId ? CustomFields.Select(Clone).ToList() : new List<UdfField>());
			var udfValues = new Mock<IUdfFieldValueRepository>();
			udfValues.Setup(r => r.GetFieldValuesByEntityAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()))
				.ReturnsAsync((int t, string e, string d) => CustomValues.Where(v => v.EntityType == t && v.EntityId == e && v.UdfDefinitionId == d).Select(Clone).ToList());

			Service = new ProtectedWorkflowService(releases, disclosures, workflows, steps, credentials, dataProtection, departments,
				permissions.Object, groups.Object, roles.Object, encryption.Object, Mock.Of<IUserProfileService>(),
				new Lazy<ICommunicationService>(() => communication.Object), udfDefinitions.Object, udfFields.Object);

			var calls = new Mock<ICallsRepository>();
			calls.Setup(c => c.GetByIdAsync(It.IsAny<object>())).ReturnsAsync((object id) => Calls.TryGetValue((int)id, out var call) ? Clone(call) : null);
			calls.Setup(c => c.TryUpdateSubjectIdentifiersAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((int callId, int departmentId, string expected, string value, CancellationToken c) =>
				{
					if (!Calls.TryGetValue(callId, out var stored) || stored.DepartmentId != departmentId || stored.SubjectIdentifiers != expected)
						return false;
					if (SubjectIdentifierWriteConflicts > 0)
					{
						SubjectIdentifierWriteConflicts--;
						return false;
					}
					stored.SubjectIdentifiers = value;
					return true;
				});

			// The REAL write pipeline (encrypt-only workload lane) over the fake broker.
			var writes = new ProtectedReadService(dataProtection, Mock.Of<IProtectedDataGrantService>(), Broker, new ProtectedFieldCatalog());

			Runtime = new ProtectedWorkflowRuntime(releases, steps, credentials, dataProtection, Broker, Service, calls.Object,
				udfValues.Object, writes);

			// The payload a workflow actually receives: the SAFE projection, with every cataloged value already REDACTED.
			var enforced = new Mock<IDepartmentDataProtectionService>();
			enforced.Setup(d => d.IsProtectionEnforcedAsync(It.IsAny<int>())).ReturnsAsync(true);
			_projection = new ProtectedProjectionService(enforced.Object, new ProtectedFieldCatalog());

			var contextBuilder = new WorkflowTemplateContextBuilder(departments, Mock.Of<IDepartmentSettingsService>(), Mock.Of<IUserProfileService>(),
				groups.Object, roles.Object, Mock.Of<IUnitsService>(), Mock.Of<IDepartmentMemberSensitiveDataService>(), Mock.Of<IDepartmentProfileMediaService>());

			var factory = new Mock<IWorkflowActionExecutorFactory>();
			factory.Setup(f => f.GetExecutor(It.IsAny<WorkflowActionType>())).Returns(() => Executor);

			WorkflowService = new WorkflowService(workflows, steps, credentials, RunRepository(), LogRepository(),
				Mock.Of<IWorkflowDailyUsageRepository>(), encryption.Object, factory.Object, contextBuilder,
				Mock.Of<ISubscriptionsService>(), Mock.Of<IRecordsExportService>(), protectedRuntime: Runtime, protectedWorkflows: Service);
		}

		// ── Scenario helpers ─────────────────────────────────────────────────────────────────────────

		/// <summary>Declares the first step's payload as text/plain (for tests whose template is not JSON). Before ActivateRelease.</summary>
		public void UsePlainText(int stepIndex = 0) =>
			Steps[stepIndex].ActionConfig = JsonConvert.SerializeObject(new { Url, ContentType = "text/plain" });

		public static ProtectedWorkflowActor SteppedUp(string userId, DateTime? at = null) =>
			new ProtectedWorkflowActor { UserId = userId, IsInteractive = true, StepUpVerifiedAtUtc = at ?? DateTime.UtcNow };

		/// <summary>An Active release over the CURRENT steps (fingerprint computed exactly as the service does).</summary>
		public WorkflowProtectedRelease ActivateRelease(params string[] fieldIds) => ActivateReleaseFor(Workflow, Host, fieldIds);

		/// <summary>An Active release of any workflow in the harness, pinned to <paramref name="host"/>.</summary>
		public WorkflowProtectedRelease ActivateReleaseFor(Workflow workflow, string host, params string[] fieldIds)
		{
			var release = new WorkflowProtectedRelease
			{
				WorkflowProtectedReleaseId = "rel-" + (Releases.Count + 1),
				WorkflowId = workflow.WorkflowId,
				DepartmentId = DepartmentId,
				State = (int)ProtectedReleaseState.Active,
				DestinationScheme = "https",
				DestinationHost = host,
				WorkflowCredentialId = CredentialId,
				RecipientType = (int)ProtectedReleaseRecipientType.CoveredEntity,
				RecipientName = "County DMH, Dynamics 365 case management",
				Purpose = "Close the loop on crisis calls",
				AckVersion = ProtectedWorkflowDefaults.WarningTextVersion,
				RequestedByUserId = AdminA,
				RequestedOn = DateTime.UtcNow.AddDays(-1),
				ApprovedByUserId = AdminA,
				ApprovedOn = DateTime.UtcNow.AddDays(-1),
				ExpiresOn = DateTime.UtcNow.AddDays(300),
				CreatedOn = DateTime.UtcNow.AddDays(-2).AddSeconds(Releases.Count),
				Version = 1
			};
			release.SetAllowedFieldIds(fieldIds.Length == 0 ? new[] { "calls.completednotes", "calls.callformdata" } : fieldIds);
			Refingerprint(release);
			Releases.Add(Clone(release));
			return release;
		}

		/// <summary>Re-binds a stored release's fingerprint to the current steps (a test shortcut for "an admin approved this exact config").</summary>
		public void Refingerprint(WorkflowProtectedRelease release)
		{
			var stored0 = Releases.FirstOrDefault(r => r.WorkflowProtectedReleaseId == release.WorkflowProtectedReleaseId);
			if (stored0 != null)
			{
				stored0.AllowsRestricted = release.AllowsRestricted;
				stored0.AllowsPart2 = release.AllowsPart2;
				stored0.AuthMethod = release.AuthMethod;
				stored0.AllowedFieldIds = release.AllowedFieldIds;
			}
			var workflow = release.WorkflowId == Workflow.WorkflowId ? Workflow : OtherWorkflows.Single(w => w.WorkflowId == release.WorkflowId);
			release.ConfigFingerprint = ProtectedWorkflowService.ComputeFingerprint(workflow, Steps.Where(st => st.WorkflowId == workflow.WorkflowId),
				release, CustomFields.Where(f => f.IsEnabled).ToList());
			var stored = Releases.FirstOrDefault(r => r.WorkflowProtectedReleaseId == release.WorkflowProtectedReleaseId);
			if (stored != null)
				stored.ConfigFingerprint = release.ConfigFingerprint;
		}

		public WorkflowProtectedRelease StoredRelease(string releaseId = null) =>
			releaseId == null
				? Releases.OrderByDescending(r => r.CreatedOn).First()
				: Releases.Single(r => r.WorkflowProtectedReleaseId == releaseId);

		/// <summary>A call custom field in the active definition, with a stored (enveloped) value for the harness call.</summary>
		public UdfField AddCustomField(string name, UdfFieldSensitivity sensitivity, string plaintext)
		{
			var field = new UdfField
			{
				UdfFieldId = "udf-" + name,
				UdfDefinitionId = CustomDefinitionId,
				Name = name,
				Label = name.Replace('_', ' '),
				IsEnabled = true,
				SortOrder = CustomFields.Count,
				Sensitivity = (int)sensitivity
			};
			CustomFields.Add(field);

			if (plaintext != null)
			{
				var envelope = "rgdp:1:1:" + Convert.ToBase64String(Encoding.UTF8.GetBytes("UDF-" + name));
				Broker.Plaintext[envelope] = plaintext;
				CustomValues.Add(new UdfFieldValue
				{
					UdfFieldValueId = "udfv-" + name,
					UdfFieldId = field.UdfFieldId,
					UdfDefinitionId = CustomDefinitionId,
					EntityType = (int)UdfEntityType.Call,
					EntityId = CallId.ToString(),
					Value = envelope
				});
			}

			return field;
		}

		/// <summary>Stores subject identifiers on the harness call as an envelope the fake broker can open.</summary>
		public void SetSubjectIdentifiers(string plaintextJson)
		{
			if (plaintextJson == null)
			{
				Calls[CallId].SubjectIdentifiers = null;
				return;
			}
			var envelope = "rgdp:1:1:" + Convert.ToBase64String(Encoding.UTF8.GetBytes("SUBJECT-" + Guid.NewGuid().ToString("N")));
			Broker.Plaintext[envelope] = plaintextJson;
			Calls[CallId].SubjectIdentifiers = envelope;
		}

		/// <summary>The call's stored subject identifiers, opened with the fake broker (null when none).</summary>
		public string StoredSubjectIdentifiersPlaintext()
		{
			var stored = Calls[CallId].SubjectIdentifiers;
			return stored != null && Broker.Plaintext.TryGetValue(stored, out var plain) ? plain : stored;
		}

		public static Call BuildCall()
		{
			return new Call
			{
				CallId = CallId,
				DepartmentId = DepartmentId,
				Number = "26-000123",
				Name = "rgdp:1:1:TkFNRQ==",
				NatureOfCall = EnvelopeNature,
				Notes = EnvelopeNotes,
				CompletedNotes = EnvelopeCompletedNotes,
				CallFormData = EnvelopeForm,
				Address = "rgdp:1:1:QUREUkVTUw==",
				State = 1,
				LoggedOn = DateTime.UtcNow.AddHours(-2),
				ClosedOn = DateTime.UtcNow
			};
		}

		/// <summary>What WorkflowEventProvider queues for an enforced department: the safe projection of the event.</summary>
		public string ClosedPayload(Call call = null) =>
			_projection.BuildSafeWorkflowPayloadAsync(DepartmentId, new CallClosedEvent { DepartmentId = DepartmentId, Call = call ?? Calls[CallId] })
				.GetAwaiter().GetResult();

		public Task<WorkflowRun> RunAsync(string payload = null, int attempt = 1, string runId = null) =>
			WorkflowService.ExecuteWorkflowAsync(Workflow.WorkflowId, payload ?? ClosedPayload(), DepartmentId, DepartmentCode, attempt, runId);

		/// <summary>Adds another workflow on the same trigger, with one API step (the event provider gives each its own run).</summary>
		public Workflow AddWorkflow(string workflowId, string name, WorkflowActionType actionType, string actionConfig, string outputTemplate)
		{
			var workflow = new Workflow
			{
				WorkflowId = workflowId,
				DepartmentId = DepartmentId,
				Name = name,
				TriggerEventType = Workflow.TriggerEventType,
				IsEnabled = true,
				MaxRetryCount = 3,
				CreatedByUserId = AdminA,
				CreatedOn = DateTime.UtcNow.AddDays(-5)
			};
			OtherWorkflows.Add(workflow);
			Steps.Add(new WorkflowStep
			{
				WorkflowStepId = workflowId + "-step-1",
				WorkflowId = workflowId,
				ActionType = (int)actionType,
				StepOrder = 1,
				IsEnabled = true,
				WorkflowCredentialId = CredentialId,
				ActionConfig = actionConfig,
				OutputTemplate = outputTemplate,
				CreatedByUserId = AdminA,
				CreatedOn = DateTime.UtcNow.AddDays(-5)
			});
			return workflow;
		}

		/// <summary>What the editor panel shows right now: the steps fingerprint a request must present back.</summary>
		public string StepsFingerprint(string tokenHost = null, string authMethod = null) =>
			ProtectedWorkflowService.ComputeStepsFingerprint(Workflow, Steps.Where(st => st.WorkflowId == Workflow.WorkflowId), Host, tokenHost, authMethod);

		/// <summary>Outcome records (the pre-send "attempted" records are separate).</summary>
		public IEnumerable<ProtectedWorkflowDisclosure> DisclosureRecords =>
			Disclosures.Where(d => d.RecordType == ProtectedWorkflowRecordTypes.Disclosure && d.Outcome != ProtectedWorkflowDisclosureOutcomes.Attempted);

		public IEnumerable<ProtectedWorkflowDisclosure> AttemptRecords =>
			Disclosures.Where(d => d.RecordType == ProtectedWorkflowRecordTypes.Disclosure && d.Outcome == ProtectedWorkflowDisclosureOutcomes.Attempted);

		public IEnumerable<ProtectedWorkflowDisclosure> AdminEvents =>
			Disclosures.Where(d => d.RecordType == ProtectedWorkflowRecordTypes.AdminEvent);

		/// <summary>Everything the platform persisted for runs, logs and the chain, flattened to text.</summary>
		public string EverythingPersisted() =>
			JsonConvert.SerializeObject(new { Runs = Runs.Values, Logs, Disclosures, Releases });

		public static T Clone<T>(T value) => value == null ? default : JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(value));

		// ── Repositories ─────────────────────────────────────────────────────────────────────────────

		private IWorkflowProtectedReleaseRepository ReleaseRepository()
		{
			var mock = new Mock<IWorkflowProtectedReleaseRepository>();
			mock.Setup(r => r.GetLatestByWorkflowIdAsync(It.IsAny<string>()))
				.ReturnsAsync((string id) => FailReleaseLookups
					? throw new InvalidOperationException("release store unavailable")
					: Clone(Releases.Where(r => r.WorkflowId == id).OrderByDescending(r => r.CreatedOn).FirstOrDefault()));
			mock.Setup(r => r.GetAllByWorkflowIdAsync(It.IsAny<string>()))
				.ReturnsAsync((string id) => Releases.Where(r => r.WorkflowId == id).Select(Clone).ToList());
			mock.Setup(r => r.GetAllByDepartmentIdAsync(It.IsAny<int>()))
				.ReturnsAsync((int id) => Releases.Where(r => r.DepartmentId == id).OrderByDescending(r => r.CreatedOn).Select(Clone).ToList());
			mock.Setup(r => r.GetAllByCredentialIdAsync(It.IsAny<string>()))
				.ReturnsAsync((string id) => Releases.Where(r => r.WorkflowCredentialId == id).Select(Clone).ToList());
			mock.Setup(r => r.GetAllByStatesAsync(It.IsAny<IEnumerable<ProtectedReleaseState>>()))
				.ReturnsAsync((IEnumerable<ProtectedReleaseState> states) => Releases.Where(r => states.Contains(r.ReleaseState)).Select(Clone).ToList());
			mock.Setup(r => r.GetByIdAsync(It.IsAny<object>()))
				.ReturnsAsync((object id) => Clone(Releases.FirstOrDefault(r => r.WorkflowProtectedReleaseId == (string)id)));
			mock.Setup(r => r.InsertAsync(It.IsAny<WorkflowProtectedRelease>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((WorkflowProtectedRelease r, CancellationToken c, bool f) => { Releases.Add(Clone(r)); return r; });
			mock.Setup(r => r.TryUpdateAsync(It.IsAny<WorkflowProtectedRelease>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((WorkflowProtectedRelease r, CancellationToken c) =>
				{
					BeforeReleaseUpdate?.Invoke(r);
					var index = Releases.FindIndex(x => x.WorkflowProtectedReleaseId == r.WorkflowProtectedReleaseId);
					if (index < 0 || Releases[index].Version != r.Version)
						return false;
					r.Version++;
					Releases[index] = Clone(r);
					return true;
				});
			mock.Setup(r => r.DeleteAsync(It.IsAny<WorkflowProtectedRelease>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((WorkflowProtectedRelease r, CancellationToken c) => Releases.RemoveAll(x => x.WorkflowProtectedReleaseId == r.WorkflowProtectedReleaseId) > 0);
			return mock.Object;
		}

		private IProtectedWorkflowDisclosureRepository DisclosureRepository()
		{
			var mock = new Mock<IProtectedWorkflowDisclosureRepository>();
			mock.Setup(r => r.AppendAsync(It.IsAny<ProtectedWorkflowDisclosure>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((ProtectedWorkflowDisclosure d, CancellationToken c) =>
				{
					if (FailDisclosureAppends)
						throw new InvalidOperationException("chain unavailable");
					d.ProtectedWorkflowDisclosureId ??= Guid.NewGuid().ToString();
					ProtectedWorkflowDisclosureChain.Link(d, Disclosures.Where(x => x.DepartmentId == d.DepartmentId).OrderBy(x => x.ChainSequence).LastOrDefault());
					Disclosures.Add(Clone(d));
					return d;
				});
			mock.Setup(r => r.GetChainForDepartmentAsync(It.IsAny<int>()))
				.ReturnsAsync((int id) => Disclosures.Where(d => d.DepartmentId == id).OrderBy(d => d.ChainSequence).Select(Clone).ToList());
			mock.Setup(r => r.GetForDepartmentAsync(It.IsAny<int>(), It.IsAny<ProtectedWorkflowDisclosureFilter>()))
				.ReturnsAsync((int id, ProtectedWorkflowDisclosureFilter f) => Disclosures
					.Where(d => d.DepartmentId == id)
					.Where(d => f?.WorkflowId == null || d.WorkflowId == f.WorkflowId)
					.Where(d => f?.EntityId == null || d.EntityId == f.EntityId)
					.Where(d => f?.RecordType == null || d.RecordType == f.RecordType)
					.OrderByDescending(d => d.ChainSequence).Select(Clone).ToList());
			return mock.Object;
		}

		private IWorkflowRepository WorkflowRepository()
		{
			var mock = new Mock<IWorkflowRepository>();
			mock.Setup(r => r.GetByIdAsync(It.IsAny<object>())).ReturnsAsync((object id) =>
				(string)id == Workflow.WorkflowId ? Clone(Workflow) : Clone(OtherWorkflows.FirstOrDefault(w => w.WorkflowId == (string)id)));
			mock.Setup(r => r.UpdateAsync(It.IsAny<Workflow>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((Workflow w, CancellationToken c, bool f) => w);
			mock.Setup(r => r.DeleteWorkflowWithAllDependenciesAsync(It.IsAny<string>()))
				.Returns((string id) => { Steps.RemoveAll(s => s.WorkflowId == id); return Task.CompletedTask; });
			return mock.Object;
		}

		private IWorkflowStepRepository StepRepository()
		{
			var mock = new Mock<IWorkflowStepRepository>();
			mock.Setup(r => r.GetAllByWorkflowIdAsync(It.IsAny<string>()))
				.ReturnsAsync((string id) => Steps.Where(s => s.WorkflowId == id).Select(Clone).ToList());
			mock.Setup(r => r.GetByIdAsync(It.IsAny<object>())).ReturnsAsync((object id) => Clone(Steps.FirstOrDefault(s => s.WorkflowStepId == (string)id)));
			mock.Setup(r => r.InsertAsync(It.IsAny<WorkflowStep>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((WorkflowStep s, CancellationToken c, bool f) => { Steps.Add(Clone(s)); return s; });
			mock.Setup(r => r.UpdateAsync(It.IsAny<WorkflowStep>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((WorkflowStep s, CancellationToken c, bool f) =>
				{
					Steps[Steps.FindIndex(x => x.WorkflowStepId == s.WorkflowStepId)] = Clone(s);
					return s;
				});
			mock.Setup(r => r.DeleteAsync(It.IsAny<WorkflowStep>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((WorkflowStep s, CancellationToken c) => Steps.RemoveAll(x => x.WorkflowStepId == s.WorkflowStepId) > 0);
			return mock.Object;
		}

		private IWorkflowCredentialRepository CredentialRepository()
		{
			var mock = new Mock<IWorkflowCredentialRepository>();
			mock.Setup(r => r.GetByIdAsync(It.IsAny<object>())).ReturnsAsync((object id) => Clone(Credentials.FirstOrDefault(c => c.WorkflowCredentialId == (string)id)));
			mock.Setup(r => r.GetAllByDepartmentIdAsync(It.IsAny<int>())).ReturnsAsync((int id) => Credentials.Where(c => c.DepartmentId == id).Select(Clone).ToList());
			mock.Setup(r => r.UpdateAsync(It.IsAny<WorkflowCredential>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((WorkflowCredential c, CancellationToken t, bool f) =>
				{
					Credentials[Credentials.FindIndex(x => x.WorkflowCredentialId == c.WorkflowCredentialId)] = Clone(c);
					return c;
				});
			mock.Setup(r => r.InsertAsync(It.IsAny<WorkflowCredential>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((WorkflowCredential c, CancellationToken t, bool f) => { Credentials.Add(Clone(c)); return c; });
			mock.Setup(r => r.DeleteAsync(It.IsAny<WorkflowCredential>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((WorkflowCredential c, CancellationToken t) => Credentials.RemoveAll(x => x.WorkflowCredentialId == c.WorkflowCredentialId) > 0);
			return mock.Object;
		}

		private IWorkflowRunRepository RunRepository()
		{
			var mock = new Mock<IWorkflowRunRepository>();
			mock.Setup(r => r.GetByIdAsync(It.IsAny<object>())).ReturnsAsync((object id) => Runs.TryGetValue((string)id, out var run) ? Clone(run) : null);
			mock.Setup(r => r.InsertAsync(It.IsAny<WorkflowRun>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((WorkflowRun r, CancellationToken c, bool f) => { Runs[r.WorkflowRunId] = Clone(r); return r; });
			mock.Setup(r => r.UpdateAsync(It.IsAny<WorkflowRun>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((WorkflowRun r, CancellationToken c, bool f) => { Runs[r.WorkflowRunId] = Clone(r); return r; });
			return mock.Object;
		}

		private IWorkflowRunLogRepository LogRepository()
		{
			var mock = new Mock<IWorkflowRunLogRepository>();
			mock.Setup(r => r.InsertAsync(It.IsAny<WorkflowRunLog>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((WorkflowRunLog l, CancellationToken c, bool f) => { Logs.Add(Clone(l)); return l; });
			return mock.Object;
		}

		private IDepartmentDataProtectionService DataProtection()
		{
			var mock = new Mock<IDepartmentDataProtectionService>();
			mock.Setup(s => s.GetPolicyByDepartmentIdAsync(It.IsAny<int>(), It.IsAny<bool>())).ReturnsAsync(() => Clone(Policy));
			mock.Setup(s => s.GetStateAsync(It.IsAny<int>(), It.IsAny<bool>())).ReturnsAsync(() => (DepartmentDataProtectionState)Policy.State);
			mock.Setup(s => s.ShouldEncryptNewWritesAsync(It.IsAny<int>())).ReturnsAsync(() => Policy.State == (int)DepartmentDataProtectionState.Enabled);
			mock.Setup(s => s.GetEgressPolicyByDepartmentIdAsync(It.IsAny<int>(), It.IsAny<bool>())).ReturnsAsync(() => Clone(Egress));
			mock.Setup(s => s.SaveEgressPolicyAsync(It.IsAny<DepartmentProtectedDataEgressPolicy>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((DepartmentProtectedDataEgressPolicy p, string user, CancellationToken c) =>
				{
					Egress = Clone(p);
					EpochBumps++;
					return p;
				});
			return mock.Object;
		}

		private IDepartmentsService Departments()
		{
			var mock = new Mock<IDepartmentsService>();
			mock.Setup(s => s.GetDepartmentByIdAsync(It.IsAny<int>(), It.IsAny<bool>())).ReturnsAsync((int id, bool b) => new Department
			{
				DepartmentId = id,
				Name = "County Behavioral Health",
				Code = DepartmentCode,
				ManagingUserId = "managing-user",
				AdminUsers = new List<string> { AdminA, AdminB }
			});
			mock.Setup(s => s.GetActiveAdminsForDepartmentAsync(It.IsAny<int>()))
				.ReturnsAsync(new List<IdentityUser> { new IdentityUser { Id = AdminA }, new IdentityUser { Id = AdminB } });
			return mock.Object;
		}
	}

	/// <summary>Broker double: decrypts known envelopes, records every call, fails on request.</summary>
	internal sealed class FakeBroker : IProtectedDataBrokerClient
	{
		public Dictionary<string, string> Plaintext { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
		public List<(int DepartmentId, string Purpose, string RequestId, List<ProtectedFieldOperationItem> Items)> Calls { get; } =
			new List<(int, string, string, List<ProtectedFieldOperationItem>)>();
		public string FailWith { get; set; }
		public bool Throw { get; set; }
		public HashSet<string> FailFields { get; } = new HashSet<string>(StringComparer.Ordinal);

		public bool IsConfigured => true;

		public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

		public Task<ProtectedDataBrokerResult> DecryptAsync(int departmentId, string grantToken, string requestId,
			IReadOnlyList<ProtectedFieldOperationItem> items, CancellationToken cancellationToken = default) =>
			throw new InvalidOperationException("Protected workflows must never use the attended lane.");

		public List<(int DepartmentId, string RequestId, List<ProtectedFieldOperationItem> Items)> Encrypts { get; } =
			new List<(int, string, List<ProtectedFieldOperationItem>)>();

		/// <summary>The encrypt-only workload lane (no grant). An attended (grant) encrypt is never expected here.</summary>
		public Task<ProtectedDataBrokerResult> EncryptAsync(int departmentId, string grantToken, string requestId,
			IReadOnlyList<ProtectedFieldOperationItem> items, CancellationToken cancellationToken = default)
		{
			if (grantToken != null)
				throw new InvalidOperationException("Protected workflows never use the attended lane.");

			Encrypts.Add((departmentId, requestId, items.Select(i => new ProtectedFieldOperationItem
			{
				FieldId = i.FieldId, RowKey = i.RowKey, CatalogVersion = i.CatalogVersion
			}).ToList()));

			var result = new ProtectedDataBrokerResult { Success = FailWith == null };
			foreach (var item in items)
			{
				var envelope = "rgdp:1:" + item.CatalogVersion + ":" + Convert.ToBase64String(Encoding.UTF8.GetBytes("ENC-" + Guid.NewGuid().ToString("N")));
				Plaintext[envelope] = item.Value;
				result.Items.Add(new ProtectedFieldOperationResult { FieldId = item.FieldId, RowKey = item.RowKey, Value = envelope });
			}
			return Task.FromResult(result);
		}

		public Task<ProtectedDataBrokerResult> DecryptForWorkloadAsync(int departmentId, string purpose, string requestId,
			IReadOnlyList<ProtectedFieldOperationItem> items, CancellationToken cancellationToken = default)
		{
			Calls.Add((departmentId, purpose, requestId, items.Select(i => new ProtectedFieldOperationItem
			{
				FieldId = i.FieldId, RowKey = i.RowKey, Value = i.Value, CatalogVersion = i.CatalogVersion
			}).ToList()));

			if (Throw)
				throw new System.Net.Http.HttpRequestException("broker unreachable");
			if (FailWith != null)
				return Task.FromResult(new ProtectedDataBrokerResult { Success = false, ErrorCode = FailWith });

			var result = new ProtectedDataBrokerResult { Success = true };
			foreach (var item in items)
			{
				var failed = FailFields.Contains(item.FieldId) || !Plaintext.ContainsKey(item.Value);
				result.Items.Add(new ProtectedFieldOperationResult
				{
					FieldId = item.FieldId,
					RowKey = item.RowKey,
					Value = failed ? null : Plaintext[item.Value],
					ErrorCode = failed ? "decrypt_failed" : null
				});
			}

			return Task.FromResult(result);
		}
	}

	/// <summary>Executor double: records every context it is handed and answers like the protected HTTP executor.</summary>
	internal sealed class CapturingExecutor : IWorkflowActionExecutor
	{
		public List<WorkflowActionContext> Calls { get; } = new List<WorkflowActionContext>();
		public Func<WorkflowActionContext, WorkflowActionResult> Respond { get; set; }
		public Exception Throw { get; set; }

		public WorkflowActionType ActionType => WorkflowActionType.CallApiPost;

		public Task<WorkflowActionResult> ExecuteAsync(WorkflowActionContext context, CancellationToken cancellationToken)
		{
			Calls.Add(context);
			if (Throw != null)
				throw Throw;
			if (Respond != null)
				return Task.FromResult(Respond(context));

			var bytes = Encoding.UTF8.GetBytes(context.RenderedContent ?? string.Empty);
			return Task.FromResult(new WorkflowActionResult
			{
				Success = true,
				ResultMessage = "HTTP 204 No Content",
				HttpStatus = 204,
				PayloadBytes = bytes.Length,
				PayloadSha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()
			});
		}
	}
}
