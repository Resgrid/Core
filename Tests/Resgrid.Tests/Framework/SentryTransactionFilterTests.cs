using FluentAssertions;
using NUnit.Framework;
using Resgrid.Framework;
using Sentry;

namespace Resgrid.Tests.Framework
{
	[TestFixture]
	public class SentryTransactionFilterTests
	{
		[TestCase("https://resgrid.example/wp-login.php")]
		[TestCase("/wp-admin/install.php")]
		[TestCase("/wordpress/wp-content/plugins/example/readme.txt")]
		[TestCase("/vendor/phpunit/phpunit/src/Util/PHP/eval-stdin.php")]
		[TestCase("/.git/config")]
		[TestCase("/%2Eenv")]
		[TestCase("/cgi-bin/status")]
		[TestCase("/legacy/default.aspx")]
		public void Known_scanner_path_is_recognized(string path)
		{
			// Act
			var result = SentryTransactionFilter.IsKnownScannerPath(path);

			// Assert
			result.Should().BeTrue();
		}

		[TestCase("/User/Home/RemovedView")]
		[TestCase("/api/v4/Calls/RemovedAction")]
		[TestCase("/events/RemovedWebhook")]
		[TestCase("/User/WordpressSettings")]
		[TestCase("/.well-known/acme-challenge/token")]
		[TestCase("/Search?q=wp-login.php")]
		public void Plausible_resgrid_path_is_not_recognized_as_scanner_noise(string path)
		{
			// Act
			var result = SentryTransactionFilter.IsKnownScannerPath(path);

			// Assert
			result.Should().BeFalse();
		}

		[Test]
		public void ShouldDrop_Scanner404_ReturnsTrue()
		{
			// Arrange
			const string requestTarget = "https://resgrid.example/wp-login.php";

			// Act
			var result = SentryTransactionFilter.ShouldDrop(SpanStatus.NotFound, requestTarget);

			// Assert
			result.Should().BeTrue();
		}

		[Test]
		public void ShouldDrop_ScannerFailureOtherThan404_ReturnsFalse()
		{
			// Arrange
			const string requestTarget = "https://resgrid.example/wp-login.php";

			// Act
			var result = SentryTransactionFilter.ShouldDrop(SpanStatus.InternalError, requestTarget);

			// Assert
			result.Should().BeFalse();
		}

		[Test]
		public void ShouldDrop_Resgrid404_ReturnsFalse()
		{
			// Arrange
			const string requestTarget = "https://resgrid.example/api/v4/Calls/RemovedAction";

			// Act
			var result = SentryTransactionFilter.ShouldDrop(SpanStatus.NotFound, requestTarget);

			// Assert
			result.Should().BeFalse();
		}
	}
}
