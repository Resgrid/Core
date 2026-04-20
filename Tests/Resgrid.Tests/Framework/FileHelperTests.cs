using FluentAssertions;
using NUnit.Framework;
using Resgrid.Framework;

namespace Resgrid.Tests.Framework
{
	[TestFixture]
	public class FileHelperTests
	{
		[TestCase("EngineeringManager.Exercise.docx.pdf", "pdf")]
		[TestCase("incident.photo.JPG", "jpg")]
		[TestCase("archive.tar.gz", "gz")]
		[TestCase("no-extension", "")]
		[TestCase("", "")]
		[TestCase(null, "")]
		public void should_return_last_file_extension_without_dot(string fileName, string expectedExtension)
		{
			var extension = FileHelper.GetFileExtensionWithoutDot(fileName);

			extension.Should().Be(expectedExtension);
		}
	}
}
