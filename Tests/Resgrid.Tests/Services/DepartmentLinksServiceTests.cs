using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Repositories;
using Resgrid.Services;
using Resgrid.Tests.Security.Live;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// DepartmentLinksService loaded LinkedDepartment by the link's own row id (DepartmentLinkId) instead of
	/// LinkedDepartmentId, so the Links page named whichever department happened to share the link's id. It now
	/// loads both ends by their department ids, and DeleteLinkAsync removes the row (the creating department
	/// had no way to revoke a link before). Deleting a link, or saving one that takes access away, rotates both
	/// parties' link-share access epochs so realtime subscriptions made before the change stop receiving events.
	/// </summary>
	[TestFixture]
	public class DepartmentLinksServiceTests
	{
		private const int LinkId = 5;
		private const int CreatorId = 10;
		private const int LinkedId = 20;

		private Mock<IDepartmentLinksRepository> _links;
		private Mock<IDepartmentsRepository> _departments;
		private InMemoryCacheProvider _cache;
		private DepartmentLinksService _service;

		[SetUp]
		public void Setup()
		{
			_links = new Mock<IDepartmentLinksRepository>();
			_departments = new Mock<IDepartmentsRepository>();

			// A department whose id equals the link's row id: the old lookup returned this one for LinkedDepartment.
			_departments.Setup(d => d.GetByIdAsync(LinkId)).ReturnsAsync(new Department { DepartmentId = LinkId, Name = "Unrelated" });
			_departments.Setup(d => d.GetByIdAsync(CreatorId)).ReturnsAsync(new Department { DepartmentId = CreatorId, Name = "Creator" });
			_departments.Setup(d => d.GetByIdAsync(LinkedId)).ReturnsAsync(new Department { DepartmentId = LinkedId, Name = "Linked" });

			_cache = new InMemoryCacheProvider();
			_service = new DepartmentLinksService(_links.Object, _departments.Object, _cache, System.Array.Empty<Resgrid.Model.Services.IDepartmentLinkAccessListener>());
		}

		private static DepartmentLink NewLink(bool enabled = false) =>
			new DepartmentLink { DepartmentLinkId = LinkId, DepartmentId = CreatorId, LinkedDepartmentId = LinkedId, LinkEnabled = enabled, DepartmentShareUnits = true };

		private async Task<(string Creator, string Linked)> Epochs() =>
			(await _service.GetShareAccessVersionAsync(CreatorId), await _service.GetShareAccessVersionAsync(LinkedId));

		[Test]
		public async Task GetLinkByIdAsync_loads_the_linked_department_by_LinkedDepartmentId()
		{
			_links.Setup(l => l.GetByIdAsync(LinkId)).ReturnsAsync(NewLink());

			var link = await _service.GetLinkByIdAsync(LinkId);

			link.Department.Name.Should().Be("Creator");
			link.LinkedDepartment.Name.Should().Be("Linked");
			_departments.Verify(d => d.GetByIdAsync(LinkId), Times.Never);
		}

		[Test]
		public async Task GetLinkByIdAsync_returns_null_for_an_unknown_link()
		{
			(await _service.GetLinkByIdAsync(404)).Should().BeNull();
		}

		[Test]
		public async Task GetAllLinksForDepartmentAsync_loads_the_linked_department_by_LinkedDepartmentId()
		{
			_links.Setup(l => l.GetAllLinksForDepartmentAsync(CreatorId)).ReturnsAsync(new List<DepartmentLink> { NewLink() });

			var links = await _service.GetAllLinksForDepartmentAsync(CreatorId);

			links.Should().ContainSingle();
			links.Single().Department.Name.Should().Be("Creator");
			links.Single().LinkedDepartment.Name.Should().Be("Linked");
			_departments.Verify(d => d.GetByIdAsync(LinkId), Times.Never);
		}

		[Test]
		public async Task DeleteLinkAsync_deletes_the_row_through_the_repository()
		{
			var link = NewLink();
			_links.Setup(l => l.DeleteAsync(link, It.IsAny<CancellationToken>())).ReturnsAsync(true);

			(await _service.DeleteLinkAsync(link, CancellationToken.None)).Should().BeTrue();

			_links.Verify(l => l.DeleteAsync(link, It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task DeleteLinkAsync_with_no_link_deletes_nothing()
		{
			(await _service.DeleteLinkAsync(null, CancellationToken.None)).Should().BeFalse();

			_links.Verify(l => l.DeleteAsync(It.IsAny<DepartmentLink>(), It.IsAny<CancellationToken>()), Times.Never);
		}

		[Test]
		public async Task DeleteLinkAsync_rotates_both_parties_access_epochs()
		{
			var before = await Epochs();
			_links.Setup(l => l.DeleteAsync(It.IsAny<DepartmentLink>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

			await _service.DeleteLinkAsync(NewLink(enabled: true), CancellationToken.None);

			var after = await Epochs();
			after.Creator.Should().NotBeNullOrEmpty().And.NotBe(before.Creator);
			after.Linked.Should().NotBeNullOrEmpty().And.NotBe(before.Linked);
		}

		[Test]
		public async Task Saving_a_link_that_takes_access_away_rotates_both_parties_access_epochs_after_the_write()
		{
			_links.Setup(l => l.GetByIdAsync(LinkId)).ReturnsAsync(NewLink(enabled: true));
			var before = await Epochs();
			(string, string)? atWrite = null;
			_links.Setup(l => l.SaveOrUpdateAsync(It.IsAny<DepartmentLink>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.Callback(() => atWrite = (_cache.GetStringAsync("deptlinkshareaccessver:" + CreatorId).Result, _cache.GetStringAsync("deptlinkshareaccessver:" + LinkedId).Result))
				.ReturnsAsync((DepartmentLink l, CancellationToken _, bool __) => l);

			await _service.SaveAsync(NewLink(enabled: false), CancellationToken.None);

			atWrite.Should().Be(before, "the epoch rotates only once the reduced link is stored");
			var after = await Epochs();
			after.Creator.Should().NotBe(before.Creator);
			after.Linked.Should().NotBe(before.Linked);
		}

		[Test]
		public async Task Saving_a_link_that_takes_nothing_away_keeps_the_access_epochs()
		{
			_links.Setup(l => l.GetByIdAsync(LinkId)).ReturnsAsync(NewLink(enabled: false));
			_links.Setup(l => l.SaveOrUpdateAsync(It.IsAny<DepartmentLink>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()))
				.ReturnsAsync((DepartmentLink l, CancellationToken _, bool __) => l);
			var before = await Epochs();

			// Accepting the link, and creating a new one, only ever add access.
			await _service.SaveAsync(NewLink(enabled: true), CancellationToken.None);
			await _service.SaveAsync(new DepartmentLink { DepartmentId = CreatorId, LinkedDepartmentId = LinkedId }, CancellationToken.None);

			(await Epochs()).Should().Be(before);
		}

		[Test]
		public async Task Publishers_never_mint_an_epoch_and_get_no_share_group_until_someone_subscribes()
		{
			(await _service.GetShareGroupNameAsync(CreatorId, DepartmentLinkShareTypes.Units)).Should().BeNull();
			(await _cache.GetStringAsync("deptlinkshareaccessver:" + CreatorId)).Should().BeNull();

			var epoch = await _service.GetShareAccessVersionAsync(CreatorId);

			(await _service.GetShareGroupNameAsync(CreatorId, DepartmentLinkShareTypes.Units)).Should().Be($"DepartmentLinkShare:{CreatorId}:Units:{epoch}");
		}
	}
}
