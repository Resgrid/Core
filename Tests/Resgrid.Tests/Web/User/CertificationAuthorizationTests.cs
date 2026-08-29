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
	/// Certifications carry licence numbers and scanned documents — protected personnel data
	/// (plan 5.1, catalog v6). Every action in the family checked only that the row belonged to the
	/// caller's department, which proves tenancy, not that the caller may read THIS member's
	/// record: any member holding Profile_View could list, download, edit or delete anyone's
	/// certifications, and the reveal endpoint would decrypt them.
	///
	/// A structural guard rather than a behavioural one on purpose. The failure mode is a NEW
	/// action being added to the family without the subject check — a hole no test of the existing
	/// actions would notice — so this asserts over the whole region instead.
	/// </summary>
	[TestFixture]
	public class CertificationAuthorizationTests
	{
		/// <summary>Actions that resolve a subject from user input and must authorize it.</summary>
		private static readonly string[] GuardedActions =
		{
			"Certifications",
			"AddCertification",
			"EditCertification",
			"DeleteCertification",
			"GetCertificationData",
			"RevealCertifications"
		};

		/// <summary>The two entry points that establish "may this caller reach this member".</summary>
		private static readonly string[] Guards =
		{
			"CanReachCertificationsForAsync",
			"GetAuthorizedCertificationAsync"
		};

		private static string ControllerSource()
		{
			var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Resgrid.sln")))
				directory = directory.Parent;

			directory.Should().NotBeNull("the tests must be able to find the repository root");

			var path = Path.Combine(directory!.FullName, "Web", "Resgrid.Web", "Areas", "User",
				"Controllers", "ProfileController.cs");

			File.Exists(path).Should().BeTrue($"expected the controller at {path}");
			return File.ReadAllText(path);
		}

		/// <summary>
		/// Crude but sufficient: take everything from an action's signature to the start of the next
		/// action. Good enough to prove a guard call appears inside the body.
		/// </summary>
		private static IEnumerable<(string Action, string Body)> ActionBodies(string source)
		{
			// Every method declaration is a boundary, not just the actions — otherwise a private
			// helper sitting between two actions is absorbed into the one above it and its own
			// (legitimate) repository call reads as if the action made it.
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

		[Test]
		public void Every_certification_action_authorizes_the_subject_not_just_the_department()
		{
			var bodies = ActionBodies(ControllerSource()).ToList();

			foreach (var action in GuardedActions)
			{
				var matching = bodies.Where(b => b.Action == action).ToList();
				matching.Should().NotBeEmpty($"{action} should still exist on ProfileController");

				foreach (var body in matching)
				{
					Guards.Any(guard => body.Body.Contains(guard))
						.Should().BeTrue($"{action} reaches a member's certifications and must run the " +
							"subject authorization check, not only the department comparison");
				}
			}
		}

		[Test]
		public void No_certification_action_relies_on_a_bare_department_comparison()
		{
			// This is the exact shape that was wrong: proving the row is in the caller's tenant and
			// treating that as permission to read it.
			foreach (var body in ActionBodies(ControllerSource()).Where(b => GuardedActions.Contains(b.Action)))
			{
				body.Body.Should().NotContain("_certificationService.GetCertificationByIdAsync",
					$"{body.Action} should load through GetAuthorizedCertificationAsync so the subject " +
					"check cannot be skipped");
			}
		}
	}
}
