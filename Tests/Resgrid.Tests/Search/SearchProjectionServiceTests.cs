using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Model.Search;
using Resgrid.Model.Services;
using Resgrid.Services.Search;

namespace Resgrid.Tests.Search
{
	/// <summary>
	/// The projection allowlist (plan R2.15, R3): cataloged columns only where protection is not enforced, envelopes
	/// and the redaction placeholder never, hooks never throw, and the profile hook keeps group/active state it cannot know.
	/// </summary>
	[TestFixture]
	public class SearchProjectionServiceTests
	{
		private Mock<ISearchProjectionsRepository> _repo;
		private Mock<IDepartmentDataProtectionService> _protection;
		private SearchProjectionService _service;
		private bool _enforced;

		[SetUp]
		public void SetUp()
		{
			_enforced = false;
			_repo = new Mock<ISearchProjectionsRepository>();
			_repo.Setup(r => r.UpsertAsync(It.IsAny<SearchProjection>(), It.IsAny<CancellationToken>())).ReturnsAsync((SearchProjection p, CancellationToken _) => p);
			_protection = new Mock<IDepartmentDataProtectionService>();
			_protection.Setup(p => p.IsProtectionEnforcedAsync(It.IsAny<int>())).ReturnsAsync(() => _enforced);
			_protection.Setup(p => p.GetPinnedCatalogVersionAsync(It.IsAny<int>())).ReturnsAsync(25);
			_protection.Setup(p => p.GetPolicyByDepartmentIdAsync(It.IsAny<int>(), It.IsAny<bool>())).ReturnsAsync(new DepartmentDataProtectionPolicy { PolicyEpoch = 3 });
			_service = new SearchProjectionService(_repo.Object, _protection.Object);
		}

		private static Call Call() => new Call
		{
			CallId = 42, DepartmentId = 7, Number = "2026-000042", Name = "House Fire", NatureOfCall = "Smoke showing", Type = "Fire",
			Address = "123 Main St", IncidentNumber = "INC-9", Priority = 3, State = (int)CallStates.Active, LoggedOn = new DateTime(2026, 5, 1)
		};

		[Test]
		public async Task Unprotected_department_projects_name_address_and_identifiers()
		{
			var p = await _service.BuildCallAsync(Call());

			p.Title.Should().Be("House Fire");
			p.SearchText.Should().Contain("123 Main St");
			p.Keywords.Should().Contain("2026-000042").And.Contain("INC-9");
			p.Status.Should().Be("Active");
			p.Priority.Should().Be(3);
			p.IncludesProtectedText.Should().BeTrue();
			p.ProtectedCatalogVersion.Should().Be(25);
			p.PolicyEpoch.Should().Be(3);
			p.Url.Should().Be("/User/Dispatch/ViewCall?callId=42");
		}

		[Test]
		public async Task Enforced_department_projects_only_system_fields()
		{
			_enforced = true;
			var p = await _service.BuildCallAsync(Call());

			p.Title.Should().Be("Call 2026-000042");
			p.SearchText.Should().BeNull();
			p.Summary.Should().BeNull();
			p.Keywords.Should().Be("2026-000042");
			p.IncludesProtectedText.Should().BeFalse();
		}

		[Test]
		public async Task Envelopes_and_the_redaction_placeholder_are_never_projected()
		{
			var call = Call();
			call.Name = ProtectedDataEnvelope.Prefix + "AAAA";
			call.Address = ProtectedDataEnvelope.RedactionValue;

			var p = await _service.BuildCallAsync(call);

			p.Title.Should().Be("Call 2026-000042");
			(p.SearchText ?? string.Empty).Should().NotContain(ProtectedDataEnvelope.Prefix).And.NotContain(ProtectedDataEnvelope.RedactionValue);
		}

		[Test]
		public async Task Contacts_are_not_projected_at_all_under_enforcement()
		{
			_enforced = true;
			var p = await _service.BuildContactAsync(new Contact { ContactId = "c1", DepartmentId = 7, FirstName = "Ada", LastName = "Lovelace", CompanyName = "Analytical" });
			p.Should().BeNull();

			_enforced = false;
			var open = await _service.BuildContactAsync(new Contact { ContactId = "c1", DepartmentId = 7, FirstName = "Ada", LastName = "Lovelace", CompanyName = "Analytical", CellPhoneNumber = "(555) 010-2020" });
			open.Title.Should().Be("Ada Lovelace");
			open.Keywords.Should().Contain("5550102020");
		}

		[Test]
		public async Task Notes_are_projected_regardless_with_html_stripped()
		{
			_enforced = true;
			var p = await _service.BuildNoteAsync(new Note { NoteId = 3, DepartmentId = 7, Title = "Bay doors", Body = "<p>Door <b>two</b> sticks</p>", IsAdminOnly = true, AddedOn = new DateTime(2026, 1, 1) });

			p.Title.Should().Be("Bay doors");
			p.SearchText.Should().Be("Door two sticks");
			p.IsAdminOnly.Should().BeTrue();
		}

		[Test]
		public async Task Messages_carry_sender_and_recipients_for_query_scoping()
		{
			var p = await _service.BuildMessageAsync(new Message
			{
				MessageId = 5, DepartmentId = 7, Subject = "Trade", Body = "Can anyone cover Saturday?", SendingUserId = "u2", SentOn = new DateTime(2026, 6, 1),
				MessageRecipients = new System.Collections.Generic.List<MessageRecipient> { new MessageRecipient { UserId = "u1" }, new MessageRecipient { UserId = "u3", IsDeleted = true } }
			});

			p.OwnerUserId.Should().Be("u2");
			p.ParticipantUserIds.Should().Be("u1");
			p.Title.Should().Be("Trade");
		}

		[Test]
		public async Task Hooks_never_throw_and_soft_delete_when_nothing_is_indexable()
		{
			_repo.Setup(r => r.UpsertAsync(It.IsAny<SearchProjection>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db down"));
			Func<Task> act = () => _service.ProjectCallAsync(Call());
			await act.Should().NotThrowAsync();

			var deleted = Call();
			deleted.IsDeleted = true;
			await _service.ProjectCallAsync(deleted);
			_repo.Verify(r => r.SoftDeleteAsync(7, SearchEntityTypes.Call, "42", It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Profile_hook_keeps_group_and_active_state_it_does_not_know()
		{
			_repo.Setup(r => r.GetAsync(7, SearchEntityTypes.Personnel, "u1")).ReturnsAsync(new SearchProjection { GroupId = 4, IsActive = false });
			SearchProjection stored = null;
			_repo.Setup(r => r.UpsertAsync(It.IsAny<SearchProjection>(), It.IsAny<CancellationToken>())).Callback((SearchProjection p, CancellationToken _) => stored = p).ReturnsAsync((SearchProjection p, CancellationToken _) => p);

			await _service.ProjectPersonnelAsync(7, new UserProfile { UserId = "u1", FirstName = "Jane", LastName = "Doe" }, null, null);

			stored.Should().NotBeNull();
			stored.GroupId.Should().Be(4);
			stored.IsActive.Should().BeFalse();
			stored.Title.Should().Be("Jane Doe");
		}
	}
}
