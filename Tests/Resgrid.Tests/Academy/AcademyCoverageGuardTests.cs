using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.AdminAssist;
using Resgrid.Model.Academy;

namespace Resgrid.Tests.Academy
{
	/// <summary>
	/// The A2 coverage guard (plan §15, decision 13): every Admin Assist module and capability should map to a lesson manifest or an explicit
	/// exemption. It writes its report (the per-release docs/Academy pass works from it) and WARNS; it never fails CI.
	/// </summary>
	[TestFixture]
	public class AcademyCoverageGuardTests
	{
		public enum GuardOutcome
		{
			Pass,
			Warn
		}

		/// <summary>The guard's decision: findings warn, nothing fails. Kept separate so the never-fails rule is itself tested.</summary>
		public static GuardOutcome Decide(AcademyCoverageReport report) => report.HasFindings ? GuardOutcome.Warn : GuardOutcome.Pass;

		private static string WriteReport(AcademyCoverageReport report, string fileName = "academy-coverage.md")
		{
			var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "AcademyCoverage");
			Directory.CreateDirectory(directory);
			var path = Path.Combine(directory, fileName);
			File.WriteAllText(path, report.ToMarkdown());
			return path;
		}

		[Test]
		[Category("AcademyCoverageGuard")]
		public void Every_module_and_capability_maps_to_a_lesson_or_an_exemption()
		{
			var report = AcademyCoverage.Evaluate(new ConfigurationCatalog(), AcademyManifests.Set);
			var path = WriteReport(report);
			TestContext.AddTestAttachment(path, "Academy coverage report");
			var message = report.Summary + " Report: " + path;

			if (Decide(report) == GuardOutcome.Warn)
				Assert.Warn(message + Environment.NewLine + string.Join(Environment.NewLine,
					report.UncoveredAreas.Select(a => "module " + a).Concat(report.UnknownReferences).Concat(report.StaleExemptions).Concat(report.UncoveredCapabilities.Take(40))));
			else
				Assert.Pass(message);
		}

		[Test]
		public void The_guard_warns_on_gaps_and_never_fails()
		{
			var empty = new AcademyManifestSet();
			var report = AcademyCoverage.Evaluate(new ConfigurationCatalog(), empty);

			report.HasFindings.Should().BeTrue();
			report.UncoveredAreas.Should().NotBeEmpty();
			Decide(report).Should().Be(GuardOutcome.Warn, "a coverage gap is a release-pass task, never a red build (decision 13)");
			FluentActions.Invoking(() => WriteReport(report, "academy-coverage-selftest.md")).Should().NotThrow();
			report.ToMarkdown().Should().Contain("Modules without a lesson or exemption");
		}

		[Test]
		public void Unknown_lesson_references_and_stale_exemptions_are_reported()
		{
			var lesson = Newtonsoft.Json.JsonConvert.DeserializeObject<AcademyLessonManifest>(Newtonsoft.Json.JsonConvert.SerializeObject(AcademyManifests.FindLesson("05-calls")));
			lesson.AdminAssist.Capabilities.Add("no-such-capability");
			var set = new AcademyManifestSet
			{
				Lessons = new List<AcademyLessonManifest> { lesson },
				Exemptions = new AcademyCoverageExemptions { Capabilities = new List<AcademyCoverageExemption> { new AcademyCoverageExemption { Id = "calls", Reason = "test" } } }
			};

			var report = AcademyCoverage.Evaluate(new ConfigurationCatalog(), set);

			report.UnknownReferences.Should().Contain("05-calls: capability no-such-capability");
			report.StaleExemptions.Should().Contain("capability calls", "05-calls already covers it");
		}
	}
}
