using System.Linq;
using FluentAssertions;
using NUnit.Framework;

namespace Resgrid.Tests.Providers
{
	/// <summary>
	/// GlobalPhone is compiled against a specific PhoneNumbers assembly version. When the restore graph resolves an older
	/// one, the web and worker hosts cannot load it: every parse throws, PhoneNumberProcesserProvider swallows the
	/// exception, and every phone number (a valid +1 number included) is reported invalid — which blocks every profile
	/// save that carries a phone number. NUnit loads test assemblies with a lenient resolver, so the parsing tests pass
	/// either way; this compares the versions directly.
	/// </summary>
	[TestFixture]
	public class PhoneNumberLibraryBindingTests
	{
		[Test]
		public void Resolved_PhoneNumbers_assembly_satisfies_GlobalPhone_reference()
		{
			var required = typeof(GlobalPhone.GlobalPhone).Assembly.GetReferencedAssemblies()
				.Single(a => a.Name == "PhoneNumbers").Version;
			var resolved = typeof(PhoneNumbers.PhoneNumberUtil).Assembly.GetName().Version;

			resolved.Should().BeGreaterThanOrEqualTo(required);
		}
	}
}
