using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using NUnit.Framework;

namespace Resgrid.Tests.Web.User
{
	/// <summary>
	/// Every ADP reveal endpoint decrypts a record for a caller holding a Protected Data Grant. The
	/// grant proves the CALLER stepped up with a second factor — it is not an authorization
	/// decision about the TARGET, and treating it as one is exactly the hole found on
	/// RevealCertifications in the PR #488 review, where any member with Profile_View could
	/// decrypt another member's licence numbers.
	///
	/// Structural rather than behavioural, for the same reason as
	/// <see cref="CertificationAuthorizationTests"/>: the failure mode is a NEW Reveal endpoint
	/// added for a new page without a subject check, which no test of the existing endpoints would
	/// notice. So this discovers every "Reveal*" action across the User area and asserts each one
	/// resolves and authorizes its subject.
	/// </summary>
	[TestFixture]
	public class ProtectedRevealAuthorizationTests
	{
		private static readonly string[] Controllers =
		{
			"DispatchController.cs",
			"ContactsController.cs",
			"PersonnelController.cs",
			"UnitsController.cs",
			"HomeController.cs",
			"ProfileController.cs",
			"MessagesController.cs",
			"DocumentsController.cs",
			"CalendarController.cs"
		};

		/// <summary>
		/// Calls that answer "may this caller reach THIS record", as opposed to a department
		/// comparison, which only proves tenancy. Policy attributes are not enough on their own:
		/// they say what the caller may do in general, never to whom.
		/// </summary>
		private static readonly string[] SubjectGuards =
		{
			"CanUserViewCallAsync",
			"CanUserEditCallAsync",
			"CanUserViewUserAsync",
			"CanUserEditProfileAsync",
			"CanUserModifyUnitAsync",
			"CanUserViewUnitAsync",
			"CanUserViewMessageAsync",
			"CanReachCertificationsForAsync",
			"GetAuthorizedCertificationAsync"
		};

		private static DirectoryInfo RepositoryRoot()
		{
			var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Resgrid.sln")))
				directory = directory.Parent;

			directory.Should().NotBeNull("the tests must be able to find the repository root");
			return directory!;
		}

		/// <summary>
		/// Every method declaration is a boundary, not just the actions — otherwise a private helper
		/// between two actions is absorbed into the one above it and its calls read as the action's.
		/// </summary>
		private static IEnumerable<(string Action, string Body)> MethodBodies(string source)
		{
			var matches = Regex.Matches(source,
				@"(?:public|private|protected|internal)\s+(?:static\s+)?(?:async\s+)?Task<[^>]+>\s+(?<name>\w+)\s*\(",
				RegexOptions.Compiled).Cast<Match>().ToList();

			for (var i = 0; i < matches.Count; i++)
			{
				var start = matches[i].Index;
				var end = i + 1 < matches.Count ? matches[i + 1].Index : source.Length;
				yield return (matches[i].Groups["name"].Value, source.Substring(start, end - start));
			}
		}

		private static List<(string Controller, string Action, string Body)> RevealActions()
		{
			var root = RepositoryRoot();
			var found = new List<(string, string, string)>();

			foreach (var controller in Controllers)
			{
				var path = Path.Combine(root.FullName, "Web", "Resgrid.Web", "Areas", "User",
					"Controllers", controller);

				File.Exists(path).Should().BeTrue($"expected the controller at {path}");

				foreach (var method in MethodBodies(File.ReadAllText(path)))
				{
					if (method.Action.StartsWith("Reveal", StringComparison.Ordinal))
						found.Add((controller, method.Action, method.Body));
				}
			}

			return found;
		}

		/// <summary>
		/// Reveals of records that are department-level rather than personal. A contact belongs to
		/// the department, not to a member: there is no per-record ACL to consult, so the resource
		/// policy plus the department comparison IS the authorization model, and it is the same one
		/// the page hosting the reveal uses. Listed explicitly so that a new endpoint cannot land
		/// here by accident — adding one is a decision someone has to write down.
		/// </summary>
		private static readonly string[] DepartmentScopedReveals =
		{
			"ContactsController.cs:RevealContact",

			// A document and a calendar entry belong to the department, not to a member. There is no
			// per-record ACL to consult beyond the resource policy and, for documents, the
			// admins-only flag - which is the same model the pages hosting these reveals use.
			"DocumentsController.cs:RevealDocument",
			"CalendarController.cs:RevealCalendarItem"
		};

		[Test]
		public void Every_reveal_endpoint_authorizes_its_subject()
		{
			var actions = RevealActions();

			// If this trips, the discovery above stopped finding the endpoints (a rename, a move) —
			// the assertions below would then pass vacuously.
			actions.Should().HaveCountGreaterThanOrEqualTo(9,
				"the User area hosts reveal endpoints for calls, contacts, personnel, units, the profile page and certifications");

			foreach (var (controller, action, body) in actions)
			{
				if (DepartmentScopedReveals.Contains($"{controller}:{action}"))
				{
					body.Should().Contain("DepartmentId",
						$"{controller}.{action} reveals a department-level record, so it must at least " +
						"prove the record belongs to the caller's department");
					continue;
				}

				SubjectGuards.Any(guard => body.Contains(guard))
					.Should().BeTrue($"{controller}.{action} must authorize the subject it reveals, " +
						"not just validate the grant — a grant authorizes the caller, never the target");
			}
		}

		[Test]
		public void Every_reveal_endpoint_is_an_antiforgery_protected_post()
		{
			foreach (var (controller, action, body) in RevealActions())
			{
				// The attributes sit above the signature, so they land at the END of the PREVIOUS
				// method's slice; read the raw source around the declaration instead.
				var root = RepositoryRoot();
				var source = File.ReadAllText(Path.Combine(root.FullName, "Web", "Resgrid.Web", "Areas",
					"User", "Controllers", controller));

				var index = source.IndexOf($"> {action}(", StringComparison.Ordinal);
				index.Should().BeGreaterThan(0, $"{controller}.{action} should be declared in its own file");

				var preamble = source.Substring(Math.Max(0, index - 400), Math.Min(400, index));

				preamble.Should().Contain("[HttpPost]",
					$"{controller}.{action} reveals decrypted values and must not be reachable by a GET " +
					"(a URL lands in browser history, logs and referrers)");
				preamble.Should().Contain("[ValidateAntiForgeryToken]",
					$"{controller}.{action} is a cookie-authenticated MVC endpoint");
			}
		}
	}
}
