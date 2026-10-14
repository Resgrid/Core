using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Providers;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class CallPriorityLookupTests
	{
		private Mock<IDepartmentCallPriorityRepository> _priorities;
		private CallsService _service;

		[SetUp]
		public void SetUp()
		{
			_priorities = new Mock<IDepartmentCallPriorityRepository>();
			_service = new CallsService(
				Mock.Of<ICallsRepository>(), Mock.Of<ICommunicationService>(), Mock.Of<ICallDispatchesRepository>(),
				Mock.Of<ICallTypesRepository>(), Mock.Of<ICallEmailFactory>(), Mock.Of<ICacheProvider>(),
				Mock.Of<ICallNotesRepository>(), Mock.Of<ICallAttachmentRepository>(), Mock.Of<ICallDispatchGroupRepository>(),
				Mock.Of<ICallDispatchUnitRepository>(), Mock.Of<ICallDispatchRoleRepository>(), _priorities.Object,
				Mock.Of<IShortenUrlProvider>(), Mock.Of<ICallProtocolsRepository>(), Mock.Of<IGeoLocationProvider>(),
				Mock.Of<IDepartmentsService>(), Mock.Of<ICallReferencesRepository>(), Mock.Of<ICallContactsRepository>(),
				Mock.Of<IIndoorMapService>(), Mock.Of<ICallVideoFeedRepository>(), Mock.Of<ICallNumberingService>(),
				new Lazy<IProtectedWriteService>(() => Mock.Of<IProtectedWriteService>()));
		}

		private void Owns(int departmentId, params (int Id, string Name)[] rows)
		{
			var list = new List<DepartmentCallPriority>();
			foreach (var row in rows)
				list.Add(new DepartmentCallPriority { DepartmentCallPriorityId = row.Id, DepartmentId = departmentId, Name = row.Name });
			_priorities.Setup(x => x.GetAllByDepartmentIdAsync(departmentId)).ReturnsAsync(list);
		}

		[Test]
		public async Task a_department_owning_ids_0_to_3_gets_its_own_priority()
		{
			// Department 1 owns DepartmentCallPriorityId 0..3 (the first rows ever stored); every id up to 3 used to be read
			// as a built-in priority, so its Edit and Delete links ended at Unauthorized.
			Owns(1, (0, "Routine"), (1, "Priority"), (2, "Urgent"), (3, "Life Threat"));

			var priority = await _service.GetCallPrioritiesByIdAsync(1, 2, true);

			priority.DepartmentId.Should().Be(1);
			priority.Name.Should().Be("Urgent");
		}

		[Test]
		public async Task a_department_without_that_id_gets_the_built_in_priority()
		{
			Owns(7, (57, "Structure Fire"));

			var builtIn = await _service.GetCallPrioritiesByIdAsync(7, (int)CallPriority.High, true);
			builtIn.DepartmentId.Should().Be(0);
			builtIn.Name.Should().Be("High");

			(await _service.GetCallPrioritiesByIdAsync(7, 57, true)).Name.Should().Be("Structure Fire");
			(await _service.GetCallPrioritiesByIdAsync(7, 58, true)).Should().BeNull();
		}

		[Test]
		public async Task a_department_with_no_priorities_of_its_own_gets_the_built_ins()
		{
			Owns(9);

			(await _service.GetCallPrioritiesByIdAsync(9, (int)CallPriority.Emergency, true)).Name.Should().Be("Emergency");
		}
	}
}
