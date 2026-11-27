using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using FluentAssertions;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Events;
using Resgrid.Services;
using Resgrid.Services.Signatures;
using Resgrid.Tests.Services;
using Scriban;
using Scriban.Runtime;

namespace Resgrid.Tests.Workflows
{
	/// <summary>
	/// Round 7C: the workflow triggers of the Protocols, Training/LMS, signing and knowledge build (218-219, 230-235, 248-269). One table
	/// (<see cref="ContentWorkflowTriggers"/>) drives the catalog, samples, context and names; these tests keep the table, the payload
	/// classes and the allocation consistent, and guard that no payload can carry contact, protected, wellness or answer-key data.
	/// </summary>
	[TestFixture]
	public class ContentWorkflowTriggerTests
	{
		// Round 9A adds 275-278 (certification_event); round 9B adds 280-285 (training hours submissions and voided recorded completions); round 10 A3
		// adds 286-295 (content sets, LMS sponsorship and mutual-aid countersignature, of its block 286-297).
		private static readonly int[] Allocated = new[] { 218, 219 }.Concat(Enumerable.Range(230, 6)).Concat(Enumerable.Range(248, 22)).Concat(Enumerable.Range(270, 5))
			.Concat(Enumerable.Range(275, 4)).Concat(Enumerable.Range(280, 6)).Concat(Enumerable.Range(286, 12)).ToArray();

		private static IEnumerable<WorkflowTriggerEventType> Triggers() => ContentWorkflowTriggers.Triggers.OrderBy(t => (int)t);

		private static readonly HashSet<string> Unmapped = new HashSet<string>(StringComparer.Ordinal) { "DepartmentId", "Change", "Trigger" };

		[Test]
		public void Every_round_7C_trigger_is_in_the_allocated_blocks_and_every_allocated_value_is_registered()
		{
			ContentWorkflowTriggers.Triggers.Select(t => (int)t).Should().OnlyContain(v => Allocated.Contains(v), "7C uses only 218-219, 230-235, 248-269 and 270-274 (of 270-279); 9A 275-278; 9B 280-285");
			foreach (var value in Allocated)
			{
				Enum.IsDefined(typeof(WorkflowTriggerEventType), value).Should().BeTrue($"{value} is allocated to round 7C and used");
				ContentWorkflowTriggers.IsContent(value).Should().BeTrue($"{(WorkflowTriggerEventType)value} must be in the content table");
			}

			ContentWorkflowTriggers.Families.Select(f => f.Prefix).Should().OnlyHaveUniqueItems();
			ContentWorkflowTriggers.IsContent((int)WorkflowTriggerEventType.TrainingCompleted).Should().BeFalse("224-229 keep their own family");
			ContentWorkflowTriggers.IsContent((int)WorkflowTriggerEventType.SignatureRequested).Should().BeFalse();
		}

		[Test]
		public void Every_family_maps_every_payload_property_and_only_properties_that_exist()
		{
			foreach (var family in ContentWorkflowTriggers.Families)
			{
				var properties = family.PayloadType.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).ToList();
				var mapped = family.Variables.Select(v => v.Property).ToList();

				mapped.Should().OnlyHaveUniqueItems(family.Prefix);
				mapped.Should().OnlyContain(p => properties.Contains(p), $"{family.Prefix} maps only properties {family.PayloadType.Name} has");
				properties.Where(p => !Unmapped.Contains(p)).Should().BeSubsetOf(mapped, $"every property of {family.PayloadType.Name} is a documented variable");
				family.Variables.Should().OnlyContain(v => !string.IsNullOrWhiteSpace(v.Description) && new[] { "int", "string", "datetime", "bool", "decimal" }.Contains(v.DataType));
				if (family.UserIdProperty != null)
					properties.Should().Contain(family.UserIdProperty);
			}
		}

		[TestCaseSource(nameof(Triggers))]
		public void The_payload_raises_the_trigger_its_change_names(WorkflowTriggerEventType trigger)
		{
			var family = ContentWorkflowTriggers.For(trigger);
			var payload = Activator.CreateInstance(family.PayloadType);
			var change = family.PayloadType.GetProperty("Change");
			if (change != null)
				change.SetValue(payload, Enum.Parse(change.PropertyType, family.Changes[trigger]));
			else
				family.Changes.Should().ContainSingle("a payload without a Change property has one trigger");

			family.PayloadType.GetProperty("Trigger").GetValue(payload).Should().Be(trigger);
			family.PayloadType.GetProperty("DepartmentId").Should().NotBeNull("the provider routes by department");
		}

		[TestCaseSource(nameof(Triggers))]
		public void The_catalog_lists_change_and_every_variable_and_none_of_them_is_common(WorkflowTriggerEventType trigger)
		{
			var family = ContentWorkflowTriggers.For(trigger);
			var catalog = WorkflowTemplateVariableCatalog.GetVariableCatalog(trigger);
			var own = catalog.Where(v => v.Name.StartsWith(family.Prefix + ".", StringComparison.Ordinal)).ToList();

			own.Select(v => v.Name).Should().BeEquivalentTo(new[] { family.Prefix + ".change" }.Concat(family.Variables.Select(v => family.Prefix + "." + v.Variable)));
			own.Should().OnlyContain(v => !v.IsCommon);
			catalog.Should().Contain(v => v.Name == "user.full_name", "the common blocks stay");
		}

		[TestCaseSource(nameof(Triggers))]
		public void The_sample_data_carries_every_variable_and_renders_the_change(WorkflowTriggerEventType trigger)
		{
			var family = ContentWorkflowTriggers.For(trigger);
			var sample = (ScriptObject)WorkflowSampleDataGenerator.GenerateSampleData(trigger);
			var block = sample[family.Prefix] as ScriptObject;

			block.Should().NotBeNull();
			block.Keys.Should().BeEquivalentTo(new[] { "change" }.Concat(family.Variables.Select(v => v.Variable)));
			Template.Parse("{{ " + family.Prefix + ".change }}").Render(sample).Should().Be(family.Changes[trigger]);
		}

		[Test]
		public void Snake_case_follows_the_catalog_convention()
		{
			ContentWorkflowTriggers.ToSnakeCase("ProtocolVersionId").Should().Be("protocol_version_id");
			ContentWorkflowTriggers.ToSnakeCase("IsMajorVersion").Should().Be("is_major_version");
			ContentWorkflowTriggers.ToSnakeCase("UserId").Should().Be("user_id");
		}

		#region Privacy guards

		private static readonly string[] ForbiddenFragments =
		{
			"Email", "Phone", "Mobile", "Address", "FirstName", "LastName", "FullName", "MemberName", "SignerName", "ContactName", "QuestionText", "Answer",
			"Comment", "Note", "Narrative", "Justification", "Token", "Password", "Secret", "Content", "Html", "Json", "Exposure", "Wellness", "Health",
			"Injury", "Medical", "Clearance", "Patient", "Location", "Ssn", "Birth"
		};

		private static readonly Type[] AllowedTypes =
		{
			typeof(int), typeof(int?), typeof(long), typeof(bool), typeof(bool?), typeof(decimal), typeof(decimal?), typeof(string), typeof(DateTime), typeof(DateTime?),
			typeof(WorkflowTriggerEventType)
		};

		[Test]
		public void No_payload_carries_contact_protected_wellness_or_free_text_fields()
		{
			var protectedColumns = new ProtectedFieldCatalog().GetAll().Select(f => f.ColumnName).ToHashSet(StringComparer.OrdinalIgnoreCase);

			foreach (var family in ContentWorkflowTriggers.Families)
			{
				foreach (var property in family.PayloadType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
				{
					var name = property.Name;
					ForbiddenFragments.Should().NotContain(f => name.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0,
						$"{family.PayloadType.Name}.{name} must not carry contact details, protected values, free text or wellness data");
					(AllowedTypes.Contains(property.PropertyType) || property.PropertyType.IsEnum).Should().BeTrue(
						$"{family.PayloadType.Name}.{name} is a {property.PropertyType.Name}: payloads hold ids, names, dates, statuses and counts only, never an entity");
					protectedColumns.Should().NotContain(name, $"{family.PayloadType.Name}.{name} shares a protected-field column name, so a protected department would see it blanked");
				}
			}
		}

		[Test]
		public void No_restricted_tier_event_exists()
		{
			var restricted = new[] { "Wellness", "Exposure", "Injury", "Medical", "Clearance", "Health", "RyanWhite", "Osha" };
			foreach (var trigger in ContentWorkflowTriggers.Triggers)
				restricted.Should().NotContain(r => trigger.ToString().IndexOf(r, StringComparison.OrdinalIgnoreCase) >= 0, $"{trigger} must not be a wellness or health trigger");

			ContentWorkflowTriggers.Families.Select(f => f.PayloadType.Namespace).Should().OnlyContain(n => n == "Resgrid.Model.Events");

			// Records documents and disclosures (restricted records live there) raise no sealing event; the Records module owns them.
			DocumentSealingService.RaisesWorkflow((int)SignatureSubjectKinds.RecordsDocument).Should().BeFalse();
			DocumentSealingService.RaisesWorkflow((int)SignatureSubjectKinds.RecordsDisclosure).Should().BeFalse();
			DocumentSealingService.RaisesWorkflow((int)SignatureSubjectKinds.ProtocolVersion).Should().BeTrue();
			DocumentSealingService.RaisesWorkflow((int)SignatureSubjectKinds.SkillEvaluation).Should().BeTrue();
		}

		[Test]
		public void A_sealing_failure_sends_a_code_never_the_raw_error()
		{
			DocumentSealingService.ErrorCode("sealing_no_timestamp").Should().Be("sealing_no_timestamp");
			DocumentSealingService.ErrorCode("The remote server returned an error: (500) at https://dss.internal/sign for jane@example.com").Should().Be("sealing_error");
			DocumentSealingService.ErrorCode(null).Should().Be("sealing_error");

			var evt = DocumentSealingService.WorkflowEvent(new SealedDocument
			{
				SealedDocumentId = 7, DepartmentId = 3, SubjectKind = (int)SignatureSubjectKinds.TimeReport, SubjectId = "42", Status = (int)SealedDocumentStatuses.Failed,
				LastError = "Npgsql: password authentication failed", Attempts = 6, Title = "DTR 2026-0042", PadesLevel = (int)PadesLevels.BaselineLTA
			}, DocumentSealingWorkflowChanges.Failed, DateTime.UtcNow);

			evt.Trigger.Should().Be(WorkflowTriggerEventType.DocumentSealingFailed);
			evt.ErrorCode.Should().Be("sealing_error");
			evt.SubjectKind.Should().Be("TimeReport");
			evt.Status.Should().Be("Failed");
			JsonConvert.SerializeObject(evt).Should().NotContain("password");
		}

		#endregion Privacy guards

		#region Names, listeners

		private static string RepositoryRoot()
		{
			var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (directory != null && !System.IO.File.Exists(Path.Combine(directory.FullName, "Resgrid.sln")))
				directory = directory.Parent;
			directory.Should().NotBeNull("the tests run inside the repository");
			return directory.FullName;
		}

		[TestCase("en")]
		[TestCase("ar")]
		[TestCase("de")]
		[TestCase("el")]
		[TestCase("es")]
		[TestCase("fr")]
		[TestCase("it")]
		[TestCase("pl")]
		[TestCase("sv")]
		[TestCase("uk")]
		public void Every_trigger_has_a_translated_name_in_the_workflow_picker(string culture)
		{
			var folder = Path.Combine(RepositoryRoot(), "Core", "Resgrid.Localization", "Areas", "User", "Workflows");
			Dictionary<string, string> Read(string file) => XDocument.Load(Path.Combine(folder, file)).Root.Elements("data")
				.ToDictionary(e => (string)e.Attribute("name"), e => (string)e.Element("value"), StringComparer.Ordinal);

			var english = Read("Workflows.resx");
			var translated = Read("Workflows." + culture + ".resx");
			foreach (var trigger in ContentWorkflowTriggers.Triggers)
			{
				var key = "Trigger." + trigger;
				english.Should().ContainKey(key);
				translated.Should().ContainKey(key, culture);
				translated[key].Should().NotBeNullOrWhiteSpace();
				if (culture != "en")
					translated[key].Should().NotBe(english[key], $"{key} must be translated into {culture}");
			}
		}

		[Test]
		public void The_workflow_provider_listens_for_every_family_on_the_synchronous_rail()
		{
			var source = System.IO.File.ReadAllText(Path.Combine(RepositoryRoot(), "Providers", "Resgrid.Providers.Bus", "WorkflowEventProvider.cs"));
			foreach (var family in ContentWorkflowTriggers.Families)
			{
				source.Should().Contain("AddListener<" + family.PayloadType.Name + ">(e => HandleEvent(e.DepartmentId, e.Trigger, e))",
					$"{family.PayloadType.Name} is produced with SendMessage, so its listener must be AddListener");
				source.Should().NotContain("AddAsyncListener<" + family.PayloadType.Name + ">");
			}
		}

		#endregion Names, listeners
	}

	/// <summary>The template context of the round-7C triggers: the payload under its prefix, change from the trigger, the member in user.*.</summary>
	[TestFixture]
	public class ContentWorkflowContextTests : Resgrid.Tests.Services.WorkflowTemplateContextBuilderTests.with_the_context_builder
	{
		[Test]
		public async Task A_protocol_event_maps_under_its_prefix_with_the_change_and_the_acting_member()
		{
			var context = await BuildContext(WorkflowTriggerEventType.ProtocolVersionPublished, new ProtocolWorkflowEvent
			{
				DepartmentId = 1, Change = ProtocolWorkflowChanges.Published, ProtocolId = 42, ProtocolVersionId = 118, ProtocolCode = "SOG-12", ProtocolName = "Mayday",
				VersionLabel = "2.0", IsMajorVersion = true, UserId = "user-7", OccurredOn = new DateTime(2026, 11, 24, 15, 0, 0, DateTimeKind.Utc)
			});

			var block = (ScriptObject)context["protocol_event"];
			block["change"].Should().Be("Published");
			Convert.ToInt32(block["protocol_id"]).Should().Be(42);
			block["protocol_name"].Should().Be("Mayday");
			block["is_major_version"].Should().Be(true);
			Convert.ToInt32(block["category_id"]).Should().Be(0, "an empty id reads 0 as the other families' ids do");
			((ScriptObject)context["user"])["id"].Should().Be("user-7");
		}

		[Test]
		public async Task A_redacted_payload_keeps_its_shape_and_an_unreadable_one_maps_nothing()
		{
			var payload = JObject.FromObject(new CourseWorkflowEvent { DepartmentId = 1, Change = CourseWorkflowChanges.Completed, CourseId = 31, CourseTitle = "SCBA", UserId = "user-9" });
			payload["is_redacted"] = true;
			var context = (ScriptObject)await Sut.BuildContextAsync(1, WorkflowTriggerEventType.CourseCompleted, payload.ToString(), CancellationToken.None);

			var block = (ScriptObject)context["course_event"];
			block["change"].Should().Be("Completed");
			block["course_title"].Should().Be("SCBA");
			((ScriptObject)context["user"])["id"].Should().Be("user-9");

			var broken = (ScriptObject)await Sut.BuildContextAsync(1, WorkflowTriggerEventType.CourseCompleted, "{not json", CancellationToken.None);
			broken.ContainsKey("course_event").Should().BeFalse();
		}

		[Test]
		public async Task Call_added_carries_the_determinant_set_with_the_call_but_never_the_case_answers()
		{
			var call = Resgrid.Tests.Helpers.WorkflowHelpers.CreateTestCall();
			call.Protocols = new List<CallProtocol>
			{
				new CallProtocol { CallProtocolId = 1, DispatchProtocolId = 42, Determinant = "26-C-1", CaseJson = "{\"answers\":{\"breathing\":\"no\"}}", OverrideReason = "caller said so" },
				new CallProtocol { CallProtocolId = 2, DispatchProtocolId = 43, Determinant = "10-D-1", RemovedOn = DateTime.UtcNow }
			};

			var context = await BuildContext(WorkflowTriggerEventType.CallAdded, new CallAddedEvent { DepartmentId = 1, Call = call });

			((ScriptObject)context["call"])["determinant"].Should().Be("26-C-1", "a removed protocol's determinant no longer applies");
			WorkflowTemplateVariableCatalog.GetVariableCatalog(WorkflowTriggerEventType.CallAdded).Should().Contain(v => v.Name == "call.determinant");
			((ScriptObject)WorkflowSampleDataGenerator.GenerateSampleData(WorkflowTriggerEventType.CallAdded))["call"].As<ScriptObject>().ContainsKey("determinant").Should().BeTrue();
		}

		[Test]
		public async Task A_family_without_a_member_leaves_the_user_block_empty()
		{
			var context = await BuildContext(WorkflowTriggerEventType.KnowledgeGapRecorded, new KnowledgeGapWorkflowEvent
			{
				DepartmentId = 1, KnowledgeGapId = 33, GapReason = "NotCovered", Surface = "Library", NearestSourceCount = 2, AskedOn = DateTime.UtcNow, OccurredOn = DateTime.UtcNow
			});

			((ScriptObject)context["knowledge_gap"])["gap_reason"].Should().Be("NotCovered");
			((ScriptObject)context["user"])["id"].Should().Be(string.Empty, "the asker is never named");
		}
	}

	/// <summary>A knowledge gap (258): ids, reason, surface and a count once the gap is stored; never the question or the asker.</summary>
	[TestFixture]
	public class KnowledgeGapWorkflowEventTests
	{
		[Test]
		public async Task A_recorded_gap_raises_one_event_without_the_question_and_a_failed_insert_raises_none()
		{
			var raised = new List<KnowledgeGapWorkflowEvent>();
			var events = new Mock<Resgrid.Model.Providers.IEventAggregator>();
			events.Setup(e => e.SendMessage(It.IsAny<KnowledgeGapWorkflowEvent>())).Callback((KnowledgeGapWorkflowEvent e) => raised.Add(e));
			var repository = new Mock<Resgrid.Model.Repositories.IKnowledgeGapsRepository>();
			repository.Setup(r => r.InsertAsync(It.IsAny<KnowledgeGap>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync((KnowledgeGap g, CancellationToken _) => { g.KnowledgeGapId = 33; return g; });
			var service = new Resgrid.Services.Knowledge.KnowledgeGapsService(repository.Object, Mock.Of<Resgrid.Model.Services.IProtocolAccessService>(),
				Mock.Of<Resgrid.Model.Services.IAuthorizationService>(), events.Object);

			await service.RecordAsync(new KnowledgeGap
			{
				DepartmentId = 5, UserId = "asker-1", AskerRole = "Firefighter", Question = "What dose of epinephrine for the patient at 12 Elm St?",
				Reason = (int)KnowledgeGapReasons.NotCovered, Surface = (int)KnowledgeAskSurfaces.Call, NearestSourcesJson = "[{\"id\":1},{\"id\":2}]"
			});

			raised.Should().ContainSingle();
			var gap = raised.Single();
			(gap.DepartmentId, gap.KnowledgeGapId, gap.GapReason, gap.Surface, gap.NearestSourceCount).Should().Be((5, 33, "NotCovered", "Call", 2));
			var json = JsonConvert.SerializeObject(gap);
			json.Should().NotContain("Elm").And.NotContain("asker-1").And.NotContain("Firefighter");

			raised.Clear();
			repository.Setup(r => r.InsertAsync(It.IsAny<KnowledgeGap>(), It.IsAny<CancellationToken>())).ReturnsAsync((KnowledgeGap)null);
			await service.RecordAsync(new KnowledgeGap { DepartmentId = 5, Question = "x" });
			raised.Should().BeEmpty();
		}
	}

	/// <summary>The protocol library's events (230-234, 248): once after the stored change, never on a refused one.</summary>
	[TestFixture, NonParallelizable]
	public class ProtocolLibraryWorkflowEventTests
	{
		private const int DepartmentId = ProtocolLibraryWorld.DepartmentId;
		private const string OldHtml = "<h2>Scene safety</h2><p>Stay upwind of the spill.</p>";
		private const string NewHtml = "<h2>Scene safety</h2><p>Stay uphill and upwind of the spill.</p>";

		private ProtocolLibraryWorld _world;
		private List<ProtocolWorkflowEvent> _raised;
		private bool _doNotBroadcast;

		[SetUp]
		public void SetUp()
		{
			_doNotBroadcast = Resgrid.Config.SystemBehaviorConfig.DoNotBroadcast;
			Resgrid.Config.SystemBehaviorConfig.DoNotBroadcast = false;
			_world = new ProtocolLibraryWorld();
			_raised = new List<ProtocolWorkflowEvent>();
			_world.Events.Setup(x => x.SendMessage(It.IsAny<ProtocolWorkflowEvent>())).Callback((ProtocolWorkflowEvent e) => _raised.Add(e));
			_world.Departments.Setup(x => x.GetDepartmentByIdAsync(DepartmentId, It.IsAny<bool>()))
				.ReturnsAsync(new Department { DepartmentId = DepartmentId, Name = "Mesa Flats", TimeZone = "Eastern Standard Time" });
			_world.Departments.Setup(x => x.GetActiveMemberUserIdsAsync(DepartmentId))
				.ReturnsAsync(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "owner-1", "admin-1" });
			_world.Departments.Setup(x => x.GetActiveAdminsForDepartmentAsync(DepartmentId))
				.ReturnsAsync(new List<Resgrid.Model.Identity.IdentityUser> { new Resgrid.Model.Identity.IdentityUser { Id = "admin-1" } });
		}

		[TearDown]
		public void TearDown() => Resgrid.Config.SystemBehaviorConfig.DoNotBroadcast = _doNotBroadcast;

		private void PublishedWithApprovedRevision()
		{
			var protocol = _world.AddProtocol(1, ProtocolStatuses.Published, currentVersionId: 90);
			protocol.ProtocolText = OldHtml;
			_world.AddVersion(90, 1, ProtocolVersionStatuses.Published, OldHtml, 1, 0);
			_world.AddApprovedVersion(100, 1, NewHtml);
		}

		[Test]
		public async Task Publishing_now_raises_published_once_with_the_version_and_a_refused_publish_raises_nothing()
		{
			PublishedWithApprovedRevision();
			var summary = _world.Versions[100].ChangeSummary;
			_world.Versions[100].ChangeSummary = null;

			(await _world.Service.PublishAsync(ProtocolLibraryWorld.Manager(), 100, false, "   ", null, null)).Succeeded.Should().BeFalse("a change summary is required");
			_world.Versions[100].ChangeSummary = summary;
			(await _world.Service.PublishAsync(ProtocolLibraryWorld.Manager(), 90, false, "Again", null, null)).Succeeded.Should().BeFalse("90 is not approved");
			_raised.Should().BeEmpty();

			(await _world.Service.PublishAsync(ProtocolLibraryWorld.Manager(), 100, false, "Uphill as well as upwind.", null, null)).Succeeded.Should().BeTrue();

			_raised.Should().ContainSingle();
			var published = _raised.Single();
			published.Trigger.Should().Be(WorkflowTriggerEventType.ProtocolVersionPublished);
			published.DepartmentId.Should().Be(DepartmentId);
			published.ProtocolId.Should().Be(1);
			published.ProtocolVersionId.Should().Be(100);
			published.VersionLabel.Should().Be("1.1");
			published.IsMajorVersion.Should().BeFalse();
			published.ProtocolCode.Should().Be("HZ-1");
			published.UserId.Should().Be(ProtocolLibraryWorld.ManagerId);
		}

		[Test]
		public async Task A_scheduled_publish_raises_nothing_until_worker_84_puts_it_in_force()
		{
			PublishedWithApprovedRevision();
			(await _world.Service.PublishAsync(ProtocolLibraryWorld.Manager(), 100, false, "Later.", DateTime.UtcNow.AddDays(3), null)).Succeeded.Should().BeTrue();
			_raised.Should().BeEmpty();

			_world.Versions[100].EffectiveOn = DateTime.UtcNow.AddMinutes(-1);
			_world.VersionsRepository.Setup(x => x.GetDueActivationsAsync(It.IsAny<DateTime>(), It.IsAny<int>()))
				.ReturnsAsync(() => new List<ProtocolVersion> { ProtocolLibraryWorld.Copy(_world.Versions[100]) });
			(await _world.Service.ActivateDueVersionsAsync(DateTime.UtcNow)).Should().Be(1);

			_raised.Should().ContainSingle(e => e.Trigger == WorkflowTriggerEventType.ProtocolVersionPublished && e.ProtocolVersionId == 100 && e.UserId == ProtocolLibraryWorld.ManagerId);
		}

		[Test]
		public async Task Retiring_raises_retired_once_and_retiring_again_raises_nothing()
		{
			_world.AddProtocol(1, ProtocolStatuses.Published, currentVersionId: 90);
			_world.AddVersion(90, 1, ProtocolVersionStatuses.Published, OldHtml, 1, 0);

			(await _world.Service.RetireAsync(ProtocolLibraryWorld.Manager(), 1)).Succeeded.Should().BeTrue();
			(await _world.Service.RetireAsync(ProtocolLibraryWorld.Manager(), 1)).Succeeded.Should().BeTrue();

			_raised.Should().ContainSingle();
			_raised.Single().Trigger.Should().Be(WorkflowTriggerEventType.ProtocolRetired);
			_raised.Single().ProtocolVersionId.Should().Be(90);
		}

		[Test]
		public async Task Retiring_a_bulletin_raises_bulletin_withdrawn_not_retired()
		{
			_world.AddProtocol(9, ProtocolStatuses.Published, currentVersionId: 901, kind: ProtocolKinds.Bulletin);
			_world.AddVersion(901, 9, ProtocolVersionStatuses.Published, OldHtml, 1, 0);

			(await _world.Service.RetireAsync(ProtocolLibraryWorld.Manager(), 9)).Succeeded.Should().BeTrue();

			_raised.Should().ContainSingle(e => e.Trigger == WorkflowTriggerEventType.ProtocolBulletinWithdrawn && e.ProtocolKind == "Bulletin");
		}

		[Test]
		public async Task A_retire_that_loses_the_version_race_raises_nothing()
		{
			_world.AddProtocol(1, ProtocolStatuses.Published, currentVersionId: 90);
			_world.AddVersion(90, 1, ProtocolVersionStatuses.Published, OldHtml, 1, 0);
			_world.VersionsRepository.Setup(x => x.TryUpdateAsync(It.IsAny<ProtocolVersion>(), It.IsAny<long>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

			await _world.Service.RetireAsync(ProtocolLibraryWorld.Manager(), 1);

			_raised.Should().BeEmpty("another request or worker retired the version first and raised it");
		}

		[Test]
		public async Task Expiry_raises_expired_once_with_no_acting_member()
		{
			_world.AddProtocol(1, ProtocolStatuses.Published, currentVersionId: 90);
			var expiring = _world.AddVersion(90, 1, ProtocolVersionStatuses.Published, OldHtml, 1, 0);
			expiring.ExpiresOn = DateTime.UtcNow.AddMinutes(-1);
			_world.VersionsRepository.Setup(x => x.GetDueExpirationsAsync(It.IsAny<DateTime>(), It.IsAny<int>()))
				.ReturnsAsync(() => new List<ProtocolVersion> { ProtocolLibraryWorld.Copy(_world.Versions[90]) });

			(await _world.Service.ExpireDueVersionsAsync(DateTime.UtcNow)).Should().Be(1);

			_raised.Should().ContainSingle();
			_raised.Single().Trigger.Should().Be(WorkflowTriggerEventType.ProtocolVersionExpired);
			_raised.Single().UserId.Should().BeNull();
		}

		[Test]
		public async Task Review_reminders_raise_due_or_overdue_per_claimed_stage_and_a_refused_claim_raises_nothing()
		{
			var now = DateTime.UtcNow;
			_world.Library.Setup(x => x.GetReviewRemindersDueAsync(It.IsAny<DateTime>(), It.IsAny<int>())).ReturnsAsync(new List<Resgrid.Model.Repositories.ProtocolReviewDue>
			{
				new Resgrid.Model.Repositories.ProtocolReviewDue { DispatchProtocolId = 1, DepartmentId = DepartmentId, Name = "Hazmat", Code = "HZ-1", OwnerUserId = "owner-1", NextReviewOn = now.AddDays(20) },
				new Resgrid.Model.Repositories.ProtocolReviewDue { DispatchProtocolId = 2, DepartmentId = DepartmentId, Name = "Rescue", Code = "RS-2", OwnerUserId = "left-member", NextReviewOn = now.AddDays(-2) }
			});

			await _world.Service.SendReviewRemindersAsync(now);

			_raised.Should().HaveCount(2);
			_raised.Should().ContainSingle(e => e.Trigger == WorkflowTriggerEventType.ProtocolReviewDue && e.ProtocolId == 1 && e.ReviewStage == "ThirtyDays" && e.OwnerUserId == "owner-1");
			_raised.Should().ContainSingle(e => e.Trigger == WorkflowTriggerEventType.ProtocolReviewOverdue && e.ProtocolId == 2 && e.OwnerUserId == null,
				"an owner who left is not named (inactive-member policy)");

			_raised.Clear();
			_world.Library.Setup(x => x.MarkReviewReminderAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
			await _world.Service.SendReviewRemindersAsync(now);
			_raised.Should().BeEmpty("another run claimed the stage");
		}
	}
}
