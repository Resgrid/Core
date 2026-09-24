using Autofac;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model.Repositories;
using Resgrid.Model.Services;

namespace Resgrid.Tests.Services.ProtectedWorkflows
{
	/// <summary>
	/// Autofac composition for Protected Workflows: WorkflowService now depends on the protected runtime and service,
	/// so a missing registration or a construction cycle must fail here, not at worker start. The shared test
	/// container has never registered the workflow definition repositories, so the scope supplies them.
	/// </summary>
	[TestFixture]
	public class ProtectedWorkflowCompositionTests : TestBase
	{
		[Test]
		public void protected_workflow_services_resolve_from_the_container()
		{
			using var scope = Bootstrapper.GetKernel().BeginLifetimeScope(builder =>
			{
				builder.RegisterInstance(Mock.Of<IWorkflowRepository>()).As<IWorkflowRepository>();
				builder.RegisterInstance(Mock.Of<IWorkflowStepRepository>()).As<IWorkflowStepRepository>();
				builder.RegisterInstance(Mock.Of<IWorkflowCredentialRepository>()).As<IWorkflowCredentialRepository>();
				builder.RegisterInstance(Mock.Of<IWorkflowRunLogRepository>()).As<IWorkflowRunLogRepository>();
				builder.RegisterInstance(Mock.Of<IWorkflowDailyUsageRepository>()).As<IWorkflowDailyUsageRepository>();
			});

			scope.Resolve<IWorkflowProtectedReleaseRepository>().Should().NotBeNull();
			scope.Resolve<IProtectedWorkflowDisclosureRepository>().Should().NotBeNull();
			scope.Resolve<IProtectedWorkflowService>().Should().BeOfType<Resgrid.Services.ProtectedWorkflowService>();
			scope.Resolve<IProtectedWorkflowRuntime>().Should().BeOfType<Resgrid.Services.ProtectedWorkflowRuntime>();
			scope.Resolve<IWorkflowService>().Should().NotBeNull();
		}
	}
}
