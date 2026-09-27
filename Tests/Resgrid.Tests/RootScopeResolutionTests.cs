using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Resgrid.Tests
{
	/// <summary>
	/// IUnitOfWork is InstancePerLifetimeScope, so anything resolved from the root container shares one unit of work,
	/// and one DB connection, with everything else resolved there for the life of the process. When one of those
	/// services opens a transaction, concurrent work lands on its connection ("The connection does not support
	/// MultipleActiveResultSets") and inside its transaction. Bootstrapper.GetKernel().Resolve and
	/// ServiceLocator.Current both resolve from the root.
	/// </summary>
	[TestFixture]
	public sealed class RootScopeResolutionTests
	{
		// Process-lifetime singletons the worker hosts prime at startup so their event subscriptions exist.
		private static readonly string[] AllowedWorkerRootResolutions =
		{
			"IEventAggregator", "IWorkflowEventProvider", "IOutboundEventProvider", "ICoreEventService"
		};

		/// <summary>
		/// Workers resolve from a per-run child scope instead:
		/// <c>using var scope = Bootstrapper.GetKernel().BeginLifetimeScope(); scope.Resolve&lt;T&gt;()</c>.
		/// </summary>
		[Test]
		public void Worker_source_does_not_resolve_scoped_services_from_the_root_container()
		{
			var root = RepositoryRoot();
			if (root == null)
				Assert.Ignore("Resgrid.sln not found above the test directory; the worker source is not available to scan.");

			var rootResolve = new Regex(@"GetKernel\(\)\s*\.\s*Resolve\s*[<(]");
			var allowed = new Regex(@"GetKernel\(\)\s*\.\s*Resolve<(" + string.Join("|", AllowedWorkerRootResolutions) + @")>\(\)");

			var offenders = Offenders(root, new[] { "Workers" }, line => rootResolve.IsMatch(line) && !allowed.IsMatch(line));

			Assert.That(offenders, Is.Empty,
				"Resolve these from a per-run scope (Bootstrapper.GetKernel().BeginLifetimeScope()) instead of the root container");
		}

		/// <summary>
		/// Scoped services take their dependencies through the constructor (Lazy&lt;T&gt; where the graph would otherwise
		/// cycle); singletons inject ILifetimeScope and begin a child scope per operation.
		/// </summary>
		[Test]
		public void Core_source_does_not_resolve_through_the_root_service_locator()
		{
			var root = RepositoryRoot();
			if (root == null)
				Assert.Ignore("Resgrid.sln not found above the test directory; the source is not available to scan.");

			// Only ever resolves SqlConfiguration, the query classes' sole constructor parameter, which holds no
			// connection or unit of work.
			var allowedFiles = new[] { Path.Combine(root, "Repositories", "Resgrid.Repositories.DataRepository", "Queries", "QueryList.cs") };
			var locator = new Regex(@"ServiceLocator\s*\.\s*Current\s*\.\s*GetInstance\b|GetKernel\(\)\s*\.\s*Resolve\s*[<(]");

			var offenders = Offenders(root, new[] { "Core", "Providers", "Repositories" }, locator.IsMatch, allowedFiles);

			Assert.That(offenders, Is.Empty,
				"Inject these through the constructor (Lazy<T> to break a cycle; ILifetimeScope with a child scope per operation in a singleton)");
		}

		private static List<string> Offenders(string root, IEnumerable<string> directories, System.Func<string, bool> isOffending, ICollection<string> allowedFiles = null)
		{
			return directories
				.SelectMany(directory => Directory.EnumerateFiles(Path.Combine(root, directory), "*.cs", SearchOption.AllDirectories))
				.Where(file => !IsBuildOutput(file) && (allowedFiles == null || !allowedFiles.Contains(file)))
				.SelectMany(file => File.ReadLines(file)
					.Select((line, index) => (line, index))
					.Where(x => !x.line.TrimStart().StartsWith("//") && isOffending(x.line))
					.Select(x => $"{Path.GetRelativePath(root, file)}:{x.index + 1}: {x.line.Trim()}"))
				.ToList();
		}

		private static bool IsBuildOutput(string path)
		{
			var separator = Path.DirectorySeparatorChar;
			return path.Contains($"{separator}obj{separator}") || path.Contains($"{separator}bin{separator}");
		}

		private static string RepositoryRoot()
		{
			var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Resgrid.sln")))
				directory = directory.Parent;
			return directory?.FullName;
		}
	}
}
