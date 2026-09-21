using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Invoicing;
using Resgrid.Model.Search;
using Resgrid.Model.Services;
using Resgrid.Services.Search;

namespace Resgrid.Tests.Search
{
	/// <summary>
	/// Workforce &amp; Business Operations families (decision 41): deployments authorize from one batched header read per window
	/// and admit rostered members without the family claim; a deleted certification type never authorizes.
	/// </summary>
	public partial class UnifiedSearchServiceTests
	{
		private Mock<IDeploymentService> _deployments;
		private Mock<ICertificationService> _certifications;

		private void BuildWithBusinessOperations()
		{
			_deployments = new Mock<IDeploymentService>(MockBehavior.Strict);
			_certifications = new Mock<ICertificationService>();
			var permissions = new Resgrid.Services.PermissionsService(_permissions.Object, Mock.Of<IUsersService>());
			_service = new UnifiedSearchService(_global.Object, _actions.Object, _flags.Object, _auth.Object, _states.Object, _recordsSearch.Object,
				_recordsAuth.Object, _records.Object, _cutover.Object, _departments.Object, permissions, _groups.Object,
				_roles.Object, _calls.Object, _units.Object, _messages.Object, _documents.Object, _notes.Object,
				_contacts.Object, _protection.Object, _settings.Object, _projections.Object,
				deployments: new Lazy<IDeploymentService>(() => _deployments.Object),
				certifications: new Lazy<ICertificationService>(() => _certifications.Object));
		}

		private static Deployment DeploymentRow(string id, int departmentId = 7, bool deleted = false) =>
			new Deployment { DeploymentId = id, DepartmentId = departmentId, Name = "Deployment " + id, IsDeleted = deleted };

		[Test]
		public async Task A_rostered_member_without_the_claim_finds_only_the_deployments_they_are_on()
		{
			BuildWithBusinessOperations();
			Answer(Hit(SearchEntityTypes.Deployment, "d1"), Hit(SearchEntityTypes.Deployment, "d2"), Hit(SearchEntityTypes.Deployment, "d3"));
			IEnumerable<string> asked = null;
			_deployments.Setup(d => d.GetDeploymentsByIdsAsync(7, It.IsAny<IEnumerable<string>>()))
				.Callback((int _, IEnumerable<string> ids) => asked = ids.ToList())
				.ReturnsAsync(new List<Deployment> { DeploymentRow("d1"), DeploymentRow("d2"), DeploymentRow("d3", deleted: true) });
			_deployments.Setup(d => d.GetDeploymentsForUserAsync(7, "u1", false)).ReturnsAsync(new List<Deployment> { DeploymentRow("d1"), DeploymentRow("d3") });

			var result = await _service.SearchAsync(new UnifiedSearchRequest { Text = "ridge" }, Principal());

			_lastQuery.EntityTypes.Should().Equal(new[] { SearchEntityTypes.Deployment });
			_lastQuery.ViewerScopedEntityTypes.Should().Equal(new[] { SearchEntityTypes.Deployment },
				"without the claim the index is asked for the caller's own deployments only, so unrostered ones never fill the candidate window");
			asked.Should().BeEquivalentTo(new[] { "d1", "d2", "d3" }, "one header read covers the whole window");
			result.Hits.Select(h => h.EntityId).Should().Equal(new[] { "d1" }, "the member is rostered on d1 only; d3 is deleted");
			result.Total.Should().BeNull("a dropped candidate suppresses the total");
			_deployments.Verify(d => d.GetDeploymentsByIdsAsync(7, It.IsAny<IEnumerable<string>>()), Times.Once);
			_deployments.Verify(d => d.GetDeploymentsForUserAsync(7, "u1", false), Times.Once);
		}

		[Test]
		public async Task A_claim_holder_authorizes_deployments_from_the_batch_without_a_roster_read()
		{
			BuildWithBusinessOperations();
			Answer(Hit(SearchEntityTypes.Deployment, "d1"), Hit(SearchEntityTypes.Deployment, "d2"));
			_deployments.Setup(d => d.GetDeploymentsByIdsAsync(7, It.IsAny<IEnumerable<string>>()))
				.ReturnsAsync(new List<Deployment> { DeploymentRow("d1"), DeploymentRow("d2", departmentId: 8) });

			var result = await _service.SearchAsync(new UnifiedSearchRequest { Text = "ridge" }, Principal("Deployments:View"));

			result.Hits.Select(h => h.EntityId).Should().Equal(new[] { "d1" }, "d2 belongs to another department");
			_lastQuery.ViewerScopedEntityTypes.Should().BeNull("a claim holder searches the whole family");
			_deployments.Verify(d => d.GetDeploymentsForUserAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
			_deployments.Verify(d => d.GetDeploymentByIdAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never, "no per-hit aggregate load");
		}

		[Test]
		public async Task A_failed_deployment_batch_fails_closed()
		{
			BuildWithBusinessOperations();
			Answer(Hit(SearchEntityTypes.Deployment, "d1"));
			_deployments.Setup(d => d.GetDeploymentsByIdsAsync(7, It.IsAny<IEnumerable<string>>())).ThrowsAsync(new InvalidOperationException("db"));

			var result = await _service.SearchAsync(new UnifiedSearchRequest { Text = "ridge" }, Principal("Deployments:View"));

			result.Hits.Should().BeEmpty();
			result.Total.Should().BeNull();
		}

		[Test]
		public async Task A_deleted_certification_type_is_never_authorized()
		{
			BuildWithBusinessOperations();
			Answer(Hit(SearchEntityTypes.CertificationType, "31"), Hit(SearchEntityTypes.CertificationType, "32"));
			_certifications.Setup(c => c.GetCertificationTypeByIdAsync(31)).ReturnsAsync(new DepartmentCertificationType { DepartmentCertificationTypeId = 31, DepartmentId = 7, Type = "FF1" });
			_certifications.Setup(c => c.GetCertificationTypeByIdAsync(32)).ReturnsAsync(new DepartmentCertificationType { DepartmentCertificationTypeId = 32, DepartmentId = 7, Type = "FF2", IsDeleted = true });

			var result = await _service.SearchAsync(new UnifiedSearchRequest { Text = "ff" }, Principal("Certifications:View"));

			result.Hits.Select(h => h.EntityId).Should().Equal("31");
			result.Total.Should().BeNull();
		}
	}
}
