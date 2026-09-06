using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace Resgrid.Tests.Web
{
	/// <summary>
	/// Runs the headless browser scripts in Tests/Resgrid.Tests/Web (record authoring, the NERIS guided form,
	/// the ADP reveal module) as NUnit cases, one per *.test.cjs file, so they run with the C# suite locally
	/// and in CI. Each script drives the real page script in Playwright; see browser-launch.cjs for how the
	/// Playwright install and the browser channel are chosen.
	///
	/// Without node or a resolvable Playwright the cases are skipped, unless RESGRID_BROWSER_TESTS=required
	/// (CI sets it), in which case a missing browser is a failure rather than a silent gap.
	/// </summary>
	[TestFixture]
	[Category("Browser")]
	[NonParallelizable]
	public class BrowserScriptTests
	{
		private static readonly TimeSpan ScriptTimeout = TimeSpan.FromMinutes(5);
		private string _skipReason;

		private static string RepositoryRoot()
		{
			var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Resgrid.sln")))
				directory = directory.Parent;
			return directory?.FullName;
		}

		private static string ScriptsDirectory()
		{
			var root = RepositoryRoot();
			return root == null ? null : Path.Combine(root, "Tests", "Resgrid.Tests", "Web");
		}

		public static IEnumerable<TestCaseData> Scripts
		{
			get
			{
				var directory = ScriptsDirectory();
				if (directory == null || !Directory.Exists(directory))
					yield break;
				foreach (var file in Directory.GetFiles(directory, "*.test.cjs").Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal))
					yield return new TestCaseData(file).SetName("{m}(" + file + ")");
			}
		}

		[OneTimeSetUp]
		public void ProbeEnvironment()
		{
			var directory = ScriptsDirectory();
			if (directory == null)
			{
				_skipReason = "Repository root (Resgrid.sln) not found above the test directory.";
				return;
			}

			var node = Run(directory, "--version", TimeSpan.FromSeconds(30));
			if (node.ExitCode != 0)
			{
				_skipReason = "node is not available on PATH: " + node.Output.Trim();
				return;
			}

			var playwright = Run(directory, "-e \"require('./browser-launch.cjs').playwright()\"", TimeSpan.FromSeconds(60));
			if (playwright.ExitCode != 0)
				_skipReason = "Playwright could not be resolved (set RESGRID_PLAYWRIGHT_PATH or run `npm ci` in Tests/Resgrid.Tests/Web): " + LastLine(playwright.Output);
		}

		[TestCaseSource(nameof(Scripts))]
		public void script_passes(string file)
		{
			if (_skipReason != null)
			{
				if (string.Equals(Environment.GetEnvironmentVariable("RESGRID_BROWSER_TESTS"), "required", StringComparison.OrdinalIgnoreCase))
					Assert.Fail("Browser tests are required in this environment but cannot run: " + _skipReason);
				Assert.Ignore(_skipReason);
			}

			var result = Run(ScriptsDirectory(), Quote(file), ScriptTimeout);
			TestContext.Out.WriteLine(result.Output);
			Assert.That(result.TimedOut, Is.False, file + " did not finish within " + ScriptTimeout);
			Assert.That(result.ExitCode, Is.EqualTo(0), file + " failed:\n" + result.Output);
		}

		private static string Quote(string value) => "\"" + value + "\"";

		/// <summary>The line that says what went wrong (node prints its version last, which says nothing).</summary>
		private static string LastLine(string output)
		{
			var lines = (output ?? string.Empty).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
			var telling = lines.FirstOrDefault(l => l.Contains("Cannot find module", StringComparison.Ordinal) || l.StartsWith("Error", StringComparison.Ordinal));
			return telling ?? (lines.Count == 0 ? string.Empty : lines[lines.Count - 1]);
		}

		private static (int ExitCode, string Output, bool TimedOut) Run(string workingDirectory, string arguments, TimeSpan timeout)
		{
			var info = new ProcessStartInfo("node", arguments)
			{
				WorkingDirectory = workingDirectory,
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				CreateNoWindow = true
			};

			var output = new StringBuilder();
			try
			{
				using var process = new Process { StartInfo = info };
				process.OutputDataReceived += (_, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
				process.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
				process.Start();
				process.BeginOutputReadLine();
				process.BeginErrorReadLine();
				if (!process.WaitForExit((int)timeout.TotalMilliseconds))
				{
					try { process.Kill(true); } catch (Exception) { }
					return (-1, output.ToString(), true);
				}
				process.WaitForExit();
				return (process.ExitCode, output.ToString(), false);
			}
			catch (Exception ex)
			{
				return (-1, output + ex.Message, false);
			}
		}
	}
}
