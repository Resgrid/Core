using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Framework;

namespace Resgrid.Tests.Framework
{
	[TestFixture]
	public class LocationHelpersTests
	{
		[TestCase("0", true)]
		[TestCase("90", true)]
		[TestCase("-90", true)]
		[TestCase("39.2733", true)]
		[TestCase("90.5", false)]
		[TestCase("120", false)]
		[TestCase("-119.5841", false)]
		[TestCase("abc", false)]
		[TestCase(null, false)]
		public void Latitude_must_be_a_decimal_between_minus_90_and_90(string value, bool valid)
		{
			LocationHelpers.IsValidLatitude(value).Should().Be(valid);
		}

		[TestCase("0", true)]
		[TestCase("180", true)]
		[TestCase("-180", true)]
		[TestCase("-119.5841", true)]
		[TestCase("180.5", false)]
		[TestCase("abc", false)]
		[TestCase(null, false)]
		public void Longitude_must_be_a_decimal_between_minus_180_and_180(string value, bool valid)
		{
			LocationHelpers.IsValidLongitude(value).Should().Be(valid);
		}

		/// <summary>
		/// The contact forms checked the exit latitude with the longitude range, so an exit latitude of 120 was accepted on
		/// both Add and Edit. A copy-pasted check reads right at a glance, so this scans for a latitude passed to the
		/// longitude check (or the reverse) anywhere in the application code.
		/// </summary>
		[Test]
		public void No_coordinate_is_checked_against_the_other_axis_range()
		{
			var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (root != null && !File.Exists(Path.Combine(root.FullName, "Resgrid.sln")))
				root = root.Parent;

			root.Should().NotBeNull("the tests must be able to find the repository root");

			var crossed = new Regex(@"IsValidLongitude\([^)]*Lat|IsValidLatitude\([^)]*Lon", RegexOptions.IgnoreCase);
			var offenders = new[] { "Core", "Web", "Providers", "Workers", "Repositories" }
				.Select(x => Path.Combine(root!.FullName, x))
				.Where(Directory.Exists)
				.SelectMany(x => Directory.EnumerateFiles(x, "*.cs", SearchOption.AllDirectories))
				.Where(x => !x.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
							!x.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
				.SelectMany(file => File.ReadAllLines(file)
					.Select((line, index) => (file, line, index))
					.Where(x => crossed.IsMatch(x.line))
					.Select(x => $"{Path.GetRelativePath(root!.FullName, x.file)}:{x.index + 1}"))
				.ToList();

			offenders.Should().BeEmpty();
		}
	}
}
