using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model.Services;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// The dedicated number services resolve from the container with their constructor dependencies (communication for the release
	/// warnings, geocoding and addresses for "near the department"), so a missing registration fails here, not in worker 80.
	/// </summary>
	[TestFixture]
	public class DedicatedNumberContainerCompositionTests : TestBase
	{
		[Test]
		public void Dedicated_number_services_resolve_from_the_container()
		{
			Resolve<IDedicatedNumberAccessService>().Should().NotBeNull();
			Resolve<IDepartmentPhoneNumbersService>().Should().NotBeNull();
		}
	}
}
