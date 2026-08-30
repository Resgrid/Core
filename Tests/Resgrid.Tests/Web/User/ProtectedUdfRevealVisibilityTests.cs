using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Model.Services;
using Resgrid.Web.Helpers;

namespace Resgrid.Tests.Web.User
{
	/// <summary>
	/// A user-defined field can be marked visible to everyone, to department and group admins, or to
	/// department admins only, and every page that renders UDF values filters on that. The reveal
	/// endpoints did not: they loaded every value for the record and returned all of them, so a
	/// caller who held a grant but was neither kind of admin could read, out of the JSON response,
	/// the values their page had deliberately left out.
	///
	/// A grant proves the caller completed a second factor. It says nothing about which fields they
	/// are allowed to see, and the two must never be conflated.
	/// </summary>
	[TestFixture]
	public class ProtectedUdfRevealVisibilityTests
	{
		private const int DeptId = 7;
		private const string EntityId = "user-1";

		private Mock<IUserDefinedFieldsService> _udfService;
		private Mock<IProtectedReadService> _protectedReadService;

		private static UdfField Field(string id) => new UdfField { UdfFieldId = id, Label = id };

		private static UdfFieldValue Value(string id, string value) =>
			new UdfFieldValue { UdfFieldId = id, Value = value };

		[SetUp]
		public void SetUp()
		{
			_udfService = new Mock<IUserDefinedFieldsService>();
			_protectedReadService = new Mock<IProtectedReadService>();

			_protectedReadService.Setup(x => x.ResolveUdfFieldValuesForReadAsync(It.IsAny<int>(),
					It.IsAny<IReadOnlyList<UdfFieldValue>>(), It.IsAny<string>(), It.IsAny<string>(),
					It.IsAny<CancellationToken>()))
				.ReturnsAsync(new ProtectedReadResult());
		}

		private void Stored(params UdfFieldValue[] values) =>
			_udfService.Setup(x => x.GetFieldValuesForEntityAsync(DeptId, It.IsAny<int>(), EntityId))
				.ReturnsAsync(values.ToList());

		private void VisibleTo(bool isDeptAdmin, bool isGroupAdmin, params UdfField[] fields) =>
			_udfService.Setup(x => x.GetVisibleFieldsForActiveDefinitionAsync(DeptId, It.IsAny<int>(),
					isDeptAdmin, isGroupAdmin))
				.ReturnsAsync(fields.ToList());

		private async Task<Dictionary<string, string>> Reveal(bool isDeptAdmin, bool isGroupAdmin)
		{
			var fields = new Dictionary<string, string>();

			await ProtectedUdfRevealHelper.AddUdfValuesAsync(fields, _udfService.Object,
				_protectedReadService.Object, DeptId, UdfEntityType.Personnel, EntityId, "grant", "caller",
				isDeptAdmin, isGroupAdmin);

			return fields;
		}

		[Test]
		public async Task A_field_the_page_hides_is_not_returned_by_the_reveal()
		{
			Stored(Value("public", "visible value"), Value("admins-only", "social security number"));
			VisibleTo(false, false, Field("public"));

			var fields = await Reveal(isDeptAdmin: false, isGroupAdmin: false);

			fields.Should().ContainKey("udffieldvalues.value:public");
			fields.Should().NotContainKey("udffieldvalues.value:admins-only",
				"a grant is step-up proof, not permission to read a field the page filtered out");
		}

		[Test]
		public async Task An_admin_still_gets_the_restricted_field()
		{
			Stored(Value("public", "visible value"), Value("admins-only", "social security number"));
			VisibleTo(true, false, Field("public"), Field("admins-only"));

			var fields = await Reveal(isDeptAdmin: true, isGroupAdmin: false);

			fields.Should().ContainKey("udffieldvalues.value:admins-only");
			fields["udffieldvalues.value:admins-only"].Should().Be("social security number");
		}

		[Test]
		public async Task A_hidden_value_is_never_even_sent_to_the_broker()
		{
			Stored(Value("public", "visible value"), Value("admins-only", "social security number"));
			VisibleTo(false, false, Field("public"));

			await Reveal(isDeptAdmin: false, isGroupAdmin: false);

			// Filtering after decryption would still be a leak of the plaintext into this process and
			// a decrypt the audit log attributes to a caller who was not allowed to ask for it.
			_protectedReadService.Verify(x => x.ResolveUdfFieldValuesForReadAsync(DeptId,
				It.Is<IReadOnlyList<UdfFieldValue>>(v => v.All(f => f.UdfFieldId == "public")),
				It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
		}

		[Test]
		public async Task Nothing_visible_means_nothing_to_reveal()
		{
			Stored(Value("admins-only", "social security number"));
			VisibleTo(false, false);

			var fields = await Reveal(isDeptAdmin: false, isGroupAdmin: false);

			fields.Should().BeEmpty();
			_protectedReadService.Verify(x => x.ResolveUdfFieldValuesForReadAsync(It.IsAny<int>(),
				It.IsAny<IReadOnlyList<UdfFieldValue>>(), It.IsAny<string>(), It.IsAny<string>(),
				It.IsAny<CancellationToken>()), Times.Never);
		}

		/// <summary>
		/// Structural, because the failure mode is a NEW reveal endpoint that forgets the flags. The
		/// helper's signature makes them mandatory, so this only has to prove that nobody satisfies
		/// the compiler by passing literal false — which would silently hide every restricted field
		/// from an admin instead, and read as working.
		/// </summary>
		[Test]
		public void Every_reveal_endpoint_passes_the_callers_real_admin_status()
		{
			var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (root != null && !System.IO.File.Exists(Path.Combine(root.FullName, "Resgrid.sln")))
				root = root.Parent;

			root.Should().NotBeNull("the tests must be able to find the repository root");

			var controllers = Directory.GetFiles(Path.Combine(root!.FullName, "Web", "Resgrid.Web",
				"Areas", "User", "Controllers"), "*.cs");

			var callers = 0;

			foreach (var path in controllers)
			{
				var source = System.IO.File.ReadAllText(path);
				if (!source.Contains("ProtectedUdfRevealHelper.AddUdfValuesAsync"))
					continue;

				callers++;

				foreach (System.Text.RegularExpressions.Match call in Regex.Matches(source,
					@"ProtectedUdfRevealHelper\.AddUdfValuesAsync\((?<args>[^;]*?)\);",
					RegexOptions.Singleline))
				{
					var args = call.Groups["args"].Value;
					var name = Path.GetFileName(path);

					args.Should().NotContain("false",
						$"{name} must pass the caller's real admin status to the reveal helper, " +
						"not a hard-coded flag");
					args.Should().Contain("isDeptAdmin",
						$"{name} must compute the caller's department-admin status the same way its page does");
					args.Should().Contain("isGroupAdmin",
						$"{name} must compute the caller's group-admin status the same way its page does");
				}
			}

			callers.Should().BeGreaterThanOrEqualTo(5,
				"contacts, calls, personnel, units and the profile page all reveal UDF values");
		}
	}
}
