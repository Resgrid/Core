using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Services.Records;

namespace Resgrid.Tests.Rms
{
	/// <summary>
	/// RMS-3 feeds into the incident report start (plan sections 4.2 and 4.3): command key times arrive as Derived
	/// facts naming Incident Command, the clear-time proxy only fills a hole dispatch left, the contact/preplan
	/// snapshot arrives as identity-and-place facts with no contact detail, and a feed failure never blocks the
	/// officer from starting the report.
	/// </summary>
	public partial class IncidentReportsServiceTests
	{
		private static readonly DateTime CommandOn = LoggedOn.AddMinutes(4);

		private IncidentCommandKeyTimes KeyTimes(DateTime? closed)
		{
			return new IncidentCommandKeyTimes
			{
				CallId = CallId, IncidentCommandId = "cmd-1", EstablishedOn = CommandOn, FirstResourceAssignedOn = CommandOn.AddMinutes(1),
				FirstBenchmarkCompletedOn = CommandOn.AddMinutes(12), LastBenchmarkCompletedOn = CommandOn.AddMinutes(30), ClosedOn = closed, MutualAidResourceCount = 2,
				Benchmarks = new List<IncidentCommandBenchmark> { new IncidentCommandBenchmark { Name = "Primary search complete", CompletedOn = CommandOn.AddMinutes(12) }, new IncidentCommandBenchmark { Name = "Fire under control", CompletedOn = CommandOn.AddMinutes(30) } },
				CapturedOn = DateTime.UtcNow
			};
		}

		[Test]
		public async Task Command_key_times_arrive_as_derived_facts_naming_incident_command()
		{
			_feeds.Setup(f => f.GetCommandKeyTimesAsync(Dept, CallId)).ReturnsAsync(KeyTimes(CommandOn.AddMinutes(45)));

			var aggregate = await _service.StartFromCallAsync(Dept, "author", CallId);

			var command = aggregate.Facts.Where(f => f.FactKey.StartsWith(NerisFactKeys.CommandPrefix, StringComparison.Ordinal)).ToList();
			command.Select(f => f.FactKey).Should().Contain(new[] { NerisFactKeys.CommandEstablished, NerisFactKeys.CommandFirstAssignment, NerisFactKeys.CommandFirstBenchmark, NerisFactKeys.CommandLastBenchmark, NerisFactKeys.CommandClosed, NerisFactKeys.CommandMutualAid, NerisFactKeys.CommandBenchmark(0), NerisFactKeys.CommandBenchmark(1) });
			command.Should().OnlyContain(f => f.SourceKind == (int)RmsSourceKind.Derived && f.SourceSystem == "IncidentCommand" && f.SourceEntityId == "cmd-1");
			command.Single(f => f.FactKey == NerisFactKeys.CommandEstablished).SourceValue.Should().Be(IncidentReportsService.Iso(CommandOn));
			command.Single(f => f.FactKey == NerisFactKeys.CommandMutualAid).SourceValue.Should().Be("2");
			command.Single(f => f.FactKey == NerisFactKeys.CommandBenchmark(1)).SourceValue.Should().StartWith("Fire under control @ ");
			_store.Facts.Should().Contain(f => f.FactKey == NerisFactKeys.CommandEstablished, "the feed facts are persisted with the report");
		}

		[Test]
		public async Task Command_close_is_the_clear_time_proxy_only_when_dispatch_recorded_none()
		{
			var closed = CommandOn.AddMinutes(45);
			_feeds.Setup(f => f.GetCommandKeyTimesAsync(Dept, CallId)).ReturnsAsync(KeyTimes(closed));

			var aggregate = await _service.StartFromCallAsync(Dept, "author", CallId);
			aggregate.Report.IncidentClearedOn.Should().Be(closed, "the Call has no ClosedOn, so the command close is the derived proxy");
			var clear = aggregate.Facts.Single(f => f.FactKey == NerisFactKeys.IncidentClear);
			clear.SourceKind.Should().Be((int)RmsSourceKind.Derived);
			clear.SourceSystem.Should().Be("IncidentCommand");
		}

		[Test]
		public async Task Dispatch_clear_time_always_wins_over_the_command_proxy()
		{
			var dispatchClosed = LoggedOn.AddMinutes(70);
			_call.ClosedOn = dispatchClosed;
			_feeds.Setup(f => f.GetCommandKeyTimesAsync(Dept, CallId)).ReturnsAsync(KeyTimes(CommandOn.AddMinutes(45)));

			var aggregate = await _service.StartFromCallAsync(Dept, "author", CallId);
			aggregate.Report.IncidentClearedOn.Should().Be(dispatchClosed);
			var clear = aggregate.Facts.Single(f => f.FactKey == NerisFactKeys.IncidentClear);
			clear.SourceKind.Should().Be((int)RmsSourceKind.Dispatch);
			aggregate.Facts.Should().ContainSingle(f => f.FactKey == NerisFactKeys.CommandClosed, "the command close is still recorded as its own fact");
		}

		[Test]
		public async Task Preplan_snapshot_arrives_as_identity_and_place_facts_without_contact_detail()
		{
			_feeds.Setup(f => f.GetPreplanSnapshotAsync(Dept, It.IsAny<Call>())).ReturnsAsync(new IncidentPreplanSnapshot
			{
				CallId = CallId,
				Contacts = new List<IncidentPreplanContact> { new IncidentPreplanContact { ContactId = "c-1", DisplayName = "Acme Storage", ContactType = "Company", CategoryName = "Warehouse", Role = "Primary" } },
				Place = new IncidentPreplanPlace { PoiId = 9, Name = "Acme Warehouse", TypeName = "Commercial", Address = "1 Main St", Latitude = 39.5, Longitude = -104.9 }
			});

			var aggregate = await _service.StartFromCallAsync(Dept, "author", CallId);

			var contact = aggregate.Facts.Single(f => f.FactKey == NerisFactKeys.PreplanContact("c-1"));
			contact.SourceKind.Should().Be((int)RmsSourceKind.Derived);
			contact.SourceSystem.Should().Be("Contacts");
			contact.SourceValue.Should().Be("Acme Storage · Company · Warehouse · Primary");
			var place = aggregate.Facts.Single(f => f.FactKey == NerisFactKeys.PreplanPlace);
			place.SourceSystem.Should().Be("Mapping");
			place.SourceEntityId.Should().Be("9");
			place.SourceValue.Should().Be("Acme Warehouse · Commercial · 1 Main St · 39.5,-104.9");
			_calls.Verify(c => c.PopulateCallData(It.IsAny<Call>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), true, It.IsAny<bool>()), Times.Once,
				"the Call's contact links are loaded so the feed can see them");
		}

		[Test]
		public async Task A_failing_feed_never_blocks_the_report_from_starting()
		{
			_feeds.Setup(f => f.GetCommandKeyTimesAsync(Dept, CallId)).ThrowsAsync(new InvalidOperationException("command store offline"));
			_feeds.Setup(f => f.GetPreplanSnapshotAsync(Dept, It.IsAny<Call>())).ThrowsAsync(new InvalidOperationException("contacts offline"));

			var aggregate = await _service.StartFromCallAsync(Dept, "author", CallId);

			aggregate.State.Should().Be(RmsRecordState.Draft);
			aggregate.Facts.Should().NotContain(f => f.FactKey.StartsWith(NerisFactKeys.CommandPrefix, StringComparison.Ordinal) || f.FactKey.StartsWith(NerisFactKeys.PreplanPrefix, StringComparison.Ordinal));
			aggregate.Facts.Should().Contain(f => f.FactKey == NerisFactKeys.CallCreate, "dispatch facts are untouched by a feed outage");
		}

		[Test]
		public async Task No_command_and_no_contacts_add_nothing()
		{
			_feeds.Setup(f => f.GetCommandKeyTimesAsync(Dept, CallId)).ReturnsAsync((IncidentCommandKeyTimes)null);

			var aggregate = await _service.StartFromCallAsync(Dept, "author", CallId);

			aggregate.Facts.Should().NotContain(f => f.FactKey.StartsWith(NerisFactKeys.CommandPrefix, StringComparison.Ordinal) || f.FactKey.StartsWith(NerisFactKeys.PreplanPrefix, StringComparison.Ordinal));
			aggregate.Report.IncidentClearedOn.Should().BeNull();
		}
	}

	/// <summary>The feed service itself: snapshots with provenance, department isolation, no contact detail, outages swallowed.</summary>
	[TestFixture]
	public class IncidentSourceFeedServiceTests
	{
		private const int Dept = 42;
		private Mock<IIncidentReportingService> _reporting;
		private Mock<IIncidentCommandService> _commands;
		private Mock<IContactsService> _contacts;
		private Mock<IMappingService> _mapping;
		private IncidentSourceFeedService _service;

		[SetUp]
		public void SetUp()
		{
			_reporting = new Mock<IIncidentReportingService>();
			_commands = new Mock<IIncidentCommandService>();
			_contacts = new Mock<IContactsService>();
			_mapping = new Mock<IMappingService>();
			_service = new IncidentSourceFeedService(_reporting.Object, _commands.Object, _contacts.Object, _mapping.Object);
		}

		[Test]
		public async Task Key_times_are_a_snapshot_of_the_command_times_report()
		{
			var established = new DateTime(2026, 9, 1, 8, 5, 0, DateTimeKind.Utc);
			_commands.Setup(c => c.GetCommandForCallAsync(Dept, 77)).ReturnsAsync(new IncidentCommand { IncidentCommandId = "cmd-1", DepartmentId = Dept, CallId = 77, EstablishedOn = established });
			_reporting.Setup(r => r.GetIncidentTimesReportAsync(Dept, 77)).ReturnsAsync(new IncidentTimesReport
			{
				CallId = 77, CommandEstablishedOn = established, FirstResourceAssignedOn = established.AddMinutes(1), LastBenchmarkCompletedOn = established.AddMinutes(30), CommandClosedOn = established.AddMinutes(60), MutualAidResourceCount = 1,
				Benchmarks = new List<BenchmarkTime> { new BenchmarkTime { Name = "Later", CompletedOn = established.AddMinutes(30) }, new BenchmarkTime { Name = "Earlier", CompletedOn = established.AddMinutes(10) }, new BenchmarkTime { Name = "Never", CompletedOn = null } }
			});

			var times = await _service.GetCommandKeyTimesAsync(Dept, 77);

			times.IncidentCommandId.Should().Be("cmd-1");
			times.EstablishedOn.Should().Be(established);
			times.ClosedOn.Should().Be(established.AddMinutes(60));
			times.MutualAidResourceCount.Should().Be(1);
			times.Benchmarks.Select(b => b.Name).Should().Equal("Earlier", "Later");
		}

		[Test]
		public async Task Another_departments_command_and_a_missing_command_yield_nothing()
		{
			_commands.Setup(c => c.GetCommandForCallAsync(Dept, 77)).ReturnsAsync(new IncidentCommand { IncidentCommandId = "cmd-1", DepartmentId = 99, CallId = 77 });
			(await _service.GetCommandKeyTimesAsync(Dept, 77)).Should().BeNull();
			_reporting.Verify(r => r.GetIncidentTimesReportAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);

			_commands.Setup(c => c.GetCommandForCallAsync(Dept, 78)).ReturnsAsync((IncidentCommand)null);
			(await _service.GetCommandKeyTimesAsync(Dept, 78)).Should().BeNull();
		}

		[Test]
		public async Task A_command_store_failure_returns_null_rather_than_throwing()
		{
			_commands.Setup(c => c.GetCommandForCallAsync(Dept, 77)).ThrowsAsync(new TimeoutException());
			(await _service.GetCommandKeyTimesAsync(Dept, 77)).Should().BeNull();
		}

		[Test]
		public async Task Preplan_snapshot_carries_identity_role_and_place_and_never_contact_detail()
		{
			var call = new Call
			{
				CallId = 77, DepartmentId = Dept, DestinationPoiId = 9,
				Contacts = new List<CallContact>
				{
					new CallContact { CallContactId = "l2", DepartmentId = Dept, CallId = 77, ContactId = "c-2", CallContactType = 1 },
					new CallContact { CallContactId = "l1", DepartmentId = Dept, CallId = 77, ContactId = "c-1", CallContactType = 0 },
					new CallContact { CallContactId = "l3", DepartmentId = Dept, CallId = 77, ContactId = "c-foreign", CallContactType = 1 }
				}
			};
			_contacts.Setup(c => c.GetContactByIdAsync("c-1")).ReturnsAsync(new Contact { ContactId = "c-1", DepartmentId = Dept, ContactType = 1, CompanyName = "Acme Storage", ContactCategoryId = "cat", Email = "private@example.invalid" });
			_contacts.Setup(c => c.GetContactByIdAsync("c-2")).ReturnsAsync(new Contact { ContactId = "c-2", DepartmentId = Dept, ContactType = 0, FirstName = "Pat", LastName = "Owner" });
			_contacts.Setup(c => c.GetContactByIdAsync("c-foreign")).ReturnsAsync(new Contact { ContactId = "c-foreign", DepartmentId = 99, ContactType = 0, FirstName = "Other", LastName = "Dept" });
			_contacts.Setup(c => c.GetContactCategoryByIdAsync("cat")).ReturnsAsync(new ContactCategory { ContactCategoryId = "cat", DepartmentId = Dept, Name = "Warehouse" });
			_mapping.Setup(m => m.GetDestinationPOIByIdAsync(Dept, 9)).ReturnsAsync(new Poi { PoiId = 9, PoiTypeId = 3, Name = "Acme Warehouse", Address = "1 Main St", Latitude = 39.5, Longitude = -104.9 });
			_mapping.Setup(m => m.GetTypeByIdAsync(3)).ReturnsAsync(new PoiType { PoiTypeId = 3, DepartmentId = Dept, Name = "Commercial" });

			var snapshot = await _service.GetPreplanSnapshotAsync(Dept, call);

			snapshot.Contacts.Select(c => c.ContactId).Should().Equal("c-1", "c-2");
			var primary = snapshot.Contacts[0];
			primary.DisplayName.Should().Be("Acme Storage");
			primary.ContactType.Should().Be("Company");
			primary.CategoryName.Should().Be("Warehouse");
			primary.Role.Should().Be("Primary");
			snapshot.Contacts[1].DisplayName.Should().Be("Pat Owner");
			snapshot.Contacts[1].Role.Should().Be("Additional");
			typeof(IncidentPreplanContact).GetProperties().Select(p => p.Name).Should().NotContain(new[] { "Email", "Phone", "PhoneNumber", "Address" }, "contact detail is a protected candidate and never enters the snapshot");
			snapshot.Place.Name.Should().Be("Acme Warehouse");
			snapshot.Place.TypeName.Should().Be("Commercial");
			snapshot.Place.Latitude.Should().Be(39.5);
		}

		[Test]
		public async Task Foreign_calls_and_missing_links_produce_an_empty_snapshot()
		{
			(await _service.GetPreplanSnapshotAsync(Dept, new Call { CallId = 1, DepartmentId = 99 })).IsEmpty.Should().BeTrue();
			(await _service.GetPreplanSnapshotAsync(Dept, new Call { CallId = 2, DepartmentId = Dept })).IsEmpty.Should().BeTrue();
			(await _service.GetPreplanSnapshotAsync(Dept, null)).IsEmpty.Should().BeTrue();
			_contacts.Verify(c => c.GetContactByIdAsync(It.IsAny<string>()), Times.Never);
		}
	}
}
