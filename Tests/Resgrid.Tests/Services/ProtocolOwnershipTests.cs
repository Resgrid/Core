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
using System;
using Resgrid.Model.Services;

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
				new DispatchProtocol { DispatchProtocolId = 1, Status = (int)ProtocolStatuses.Published, CurrentVersionId = 1000, DepartmentId = DepartmentId },
				new DispatchProtocol { DispatchProtocolId = 2, Status = (int)ProtocolStatuses.Published, CurrentVersionId = 2000, DepartmentId = DepartmentId }
			});
			_protocols.Setup(x => x.GetAllByDepartmentIdAsync(OtherDepartmentId)).ReturnsAsync(new List<DispatchProtocol>
			{
				new DispatchProtocol { DispatchProtocolId = 7, Status = (int)ProtocolStatuses.Published, CurrentVersionId = 7000, DepartmentId = OtherDepartmentId }
			});

			_service = new ProtocolsService(_protocols.Object,
				Mock.Of<IDispatchProtocolAttachmentRepository>(),
				Mock.Of<IDispatchProtocolQuestionsRepository>(),
				Mock.Of<IDispatchProtocolTriggersRepository>(),
				Mock.Of<IDispatchProtocolQuestionAnswersRepository>(), new Lazy<IDispatchTriggerFactsService>(() => Mock.Of<IDispatchTriggerFactsService>()),
				new Lazy<IProtocolAiSuggestionService>(() => Mock.Of<IProtocolAiSuggestionService>()), new Lazy<IProtectedWriteService>(() => Mock.Of<IProtectedWriteService>()));
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
				new DispatchProtocol { DispatchProtocolId = 1, Status = (int)ProtocolStatuses.Published, CurrentVersionId = 1000, DepartmentId = DepartmentId },
				new DispatchProtocol { DispatchProtocolId = 7, Status = (int)ProtocolStatuses.Published, CurrentVersionId = 7000, DepartmentId = OtherDepartmentId }
			});

			var owned = await _service.GetProtocolIdsOwnedByDepartmentAsync(DepartmentId, new[] { 1, 7 });

			owned.Should().Equal(1);
		}

		[Test]
		public async Task Drops_a_disabled_protocol()
		{
			_protocols.Setup(x => x.GetAllByDepartmentIdAsync(DepartmentId)).ReturnsAsync(new List<DispatchProtocol>
			{
				new DispatchProtocol { DispatchProtocolId = 1, Status = (int)ProtocolStatuses.Published, CurrentVersionId = 1000, DepartmentId = DepartmentId },
				new DispatchProtocol { DispatchProtocolId = 2, Status = (int)ProtocolStatuses.Published, CurrentVersionId = 2000, DepartmentId = DepartmentId, IsDisabled = true }
			});

			(await _service.GetProtocolIdsOwnedByDepartmentAsync(DepartmentId, new[] { 1, 2 })).Should().Equal(1);
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
		/// The web call forms (New Call, Update Call, Add Archived Call) read protocol ids out of the post. Every CallProtocol row
		/// they put on a call comes from the service's attach path (department, enabled, matched or picked, answers scored), never
		/// from the post directly, and nothing the post says about the code or a pass/fail flag is used.
		/// </summary>
		[Test]
		public void Every_web_call_form_builds_its_protocols_through_the_servers_attach_path()
		{
			var source = System.IO.File.ReadAllText(Path.Combine(RepositoryRoot(), "Web", "Resgrid.Web", "Areas", "User", "Controllers", "DispatchController.cs"));

			// New Call, Update Call and Add Archived Call each take the rows the service prepared, and the required-field check
			// counts only those.
			Regex.Matches(source, @"\(await PreparePostedProtocolsAsync\([^)]*\)\)\.Attached").Count.Should().Be(4);
			source.Should().Contain("_protocolsService.PrepareCallProtocolsAsync(DepartmentId, call, requests, UserId, alreadyOnCall, storeSnapshots)");

			// No CallProtocol row is built from the post, and the posted code and pending flag are never read (D15).
			source.Should().NotContain("new CallProtocol");
			source.Should().NotContain("protocolCode_");
			source.Should().NotContain("collection[key] == \"1\"");
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
			source.Should().Contain("private async Task<bool> HasAttachablePostedProtocolsAsync(Resgrid.Model.Call call, IFormCollection collection)");

			// New Call and Add Archived Call (one shared check), and Update Call, which also counts what the call already has.
			Regex.Matches(source, @"HasProtocols = ").Count.Should().Be(2);
			source.Should().Contain("var hasProtocols = policy.IsRequired(NewCallFieldKeys.Protocols) && await HasAttachablePostedProtocolsAsync(model.Call, collection);");
			source.Should().Contain("HasProtocols = hasProtocols,");
			Regex.IsMatch(source, @"HasProtocols = \(call\.Protocols\?\.Any\(x => !x\.IsRemoved\) \?\? false\) \|\|\s+\(policy\.IsRequired\(NewCallFieldKeys\.Protocols\) && await HasAttachablePostedProtocolsAsync\(")
				.Should().BeTrue("Update Call must count the call's own protocols or the ones the post would attach");
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
