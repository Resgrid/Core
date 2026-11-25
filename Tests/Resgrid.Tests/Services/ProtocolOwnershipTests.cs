using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// A protocol id that arrives from a client is attached to a call only when the protocol belongs to the call's
	/// department. Without that check, attaching another department's id let its protocol be read back through the call.
	/// </summary>
	[TestFixture]
	public class ProtocolOwnershipTests
	{
		private const int DepartmentId = 10;
		private const int OtherDepartmentId = 99;

		private Mock<IDispatchProtocolRepository> _protocols;
		private ProtocolsService _service;

		[SetUp]
		public void SetUp()
		{
			_protocols = new Mock<IDispatchProtocolRepository>();
			_protocols.Setup(x => x.GetAllByDepartmentIdAsync(DepartmentId)).ReturnsAsync(new List<DispatchProtocol>
			{
				new DispatchProtocol { DispatchProtocolId = 1, DepartmentId = DepartmentId },
				new DispatchProtocol { DispatchProtocolId = 2, DepartmentId = DepartmentId }
			});
			_protocols.Setup(x => x.GetAllByDepartmentIdAsync(OtherDepartmentId)).ReturnsAsync(new List<DispatchProtocol>
			{
				new DispatchProtocol { DispatchProtocolId = 7, DepartmentId = OtherDepartmentId }
			});

			_service = new ProtocolsService(_protocols.Object,
				Mock.Of<IDispatchProtocolAttachmentRepository>(),
				Mock.Of<IDispatchProtocolQuestionsRepository>(),
				Mock.Of<IDispatchProtocolTriggersRepository>(),
				Mock.Of<IDispatchProtocolQuestionAnswersRepository>());
		}

		[Test]
		public async Task Keeps_the_departments_own_ids_and_drops_another_departments()
		{
			var owned = await _service.GetProtocolIdsOwnedByDepartmentAsync(DepartmentId, new[] { 1, 7, 2 });

			owned.Should().Equal(1, 2);
		}

		[Test]
		public async Task Drops_an_id_that_does_not_exist()
		{
			var owned = await _service.GetProtocolIdsOwnedByDepartmentAsync(DepartmentId, new[] { 404, 2 });

			owned.Should().Equal(2);
		}

		[Test]
		public async Task Returns_each_id_once()
		{
			var owned = await _service.GetProtocolIdsOwnedByDepartmentAsync(DepartmentId, new[] { 1, 1, 2, 1 });

			owned.Should().Equal(1, 2);
		}

		[Test]
		public async Task Drops_a_row_the_repository_returned_for_the_wrong_department()
		{
			// The generic department query has returned every department's rows before; ownership never rests on it alone.
			_protocols.Setup(x => x.GetAllByDepartmentIdAsync(DepartmentId)).ReturnsAsync(new List<DispatchProtocol>
			{
				new DispatchProtocol { DispatchProtocolId = 1, DepartmentId = DepartmentId },
				new DispatchProtocol { DispatchProtocolId = 7, DepartmentId = OtherDepartmentId }
			});

			var owned = await _service.GetProtocolIdsOwnedByDepartmentAsync(DepartmentId, new[] { 1, 7 });

			owned.Should().Equal(1);
		}

		[Test]
		public async Task Asks_for_nothing_when_no_ids_were_sent()
		{
			(await _service.GetProtocolIdsOwnedByDepartmentAsync(DepartmentId, null)).Should().BeEmpty();
			(await _service.GetProtocolIdsOwnedByDepartmentAsync(DepartmentId, new int[0])).Should().BeEmpty();
			(await _service.GetProtocolIdsOwnedByDepartmentAsync(0, new[] { 1 })).Should().BeEmpty();

			_protocols.Verify(x => x.GetAllByDepartmentIdAsync(It.IsAny<int>()), Times.Never);
		}

		/// <summary>
		/// The web call forms (New Call, Update Call, Add Archived Call) read protocol ids out of the post. Each read must go
		/// through the ownership filter before a CallProtocol row is built from it.
		/// </summary>
		[Test]
		public void Every_web_call_form_filters_posted_protocol_ids_by_department()
		{
			var source = System.IO.File.ReadAllText(Path.Combine(RepositoryRoot(), "Web", "Resgrid.Web", "Areas", "User", "Controllers", "DispatchController.cs"));

			// PostedProtocolIds(collection) is only ever used as the argument of OwnedProtocolIdsAsync.
			var reads = Regex.Matches(source, @"PostedProtocolIds\(collection\)").Count;
			var filteredReads = Regex.Matches(source, @"OwnedProtocolIdsAsync\(PostedProtocolIds\(collection\)\)").Count;
			reads.Should().BeGreaterThan(0);
			filteredReads.Should().Be(reads, "a call form attached posted protocol ids without the department check");

			// New Call parses the ids itself, so both of its lists are filtered before they are used.
			source.Should().Contain("activeProtocols = await OwnedProtocolIdsAsync(activeProtocols);");
			source.Should().Contain("pendingProtocols = (await OwnedProtocolIdsAsync(pendingProtocols))");

			// And the filter itself asks the service for this department.
			source.Should().Contain("_protocolsService.GetProtocolIdsOwnedByDepartmentAsync(DepartmentId, ids)");
		}

		/// <summary>
		/// A department can require a protocol on every call. The requirement is checked against the protocols that will be
		/// attached, not the ids in the post: an id the ownership filter drops (another department's, or a protocol deleted
		/// while the form was open) used to satisfy it, and the call was saved with no protocol.
		/// </summary>
		[Test]
		public void A_required_protocol_is_checked_against_the_departments_own_posted_protocols()
		{
			var source = System.IO.File.ReadAllText(Path.Combine(RepositoryRoot(), "Web", "Resgrid.Web", "Areas", "User", "Controllers", "DispatchController.cs"));

			// Nothing reads "has protocols" off the raw form keys any more.
			source.Should().NotContain("selections.HasProtocols");
			source.Should().Contain("private async Task<bool> HasOwnedPostedProtocolsAsync(IFormCollection collection)");

			// New Call and Add Archived Call (one shared check), and Update Call, which also counts what the call already has.
			Regex.Matches(source, @"HasProtocols = ").Count.Should().Be(2);
			source.Should().Contain("var hasProtocols = policy.IsRequired(NewCallFieldKeys.Protocols) && await HasOwnedPostedProtocolsAsync(collection);");
			source.Should().Contain("HasProtocols = hasProtocols,");
			Regex.IsMatch(source, @"HasProtocols = \(call\.Protocols\?\.Any\(\) \?\? false\) \|\|\s+\(policy\.IsRequired\(NewCallFieldKeys\.Protocols\) && await HasOwnedPostedProtocolsAsync\(collection\)\)")
				.Should().BeTrue("Update Call must count the call's own protocols or the department's own posted ones");
		}

		private static string RepositoryRoot()
		{
			var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (directory != null && !System.IO.File.Exists(Path.Combine(directory.FullName, "Resgrid.sln")))
				directory = directory.Parent;
			return directory?.FullName;
		}
	}
}
