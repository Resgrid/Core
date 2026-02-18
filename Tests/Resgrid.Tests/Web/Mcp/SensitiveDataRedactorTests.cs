using NUnit.Framework;
using Resgrid.Web.Mcp.Infrastructure;

namespace Resgrid.Tests.Web.Mcp
{
	/// <summary>
	/// Unit tests for SensitiveDataRedactor to verify sensitive field redaction
	/// </summary>
	[TestFixture]
	public sealed class SensitiveDataRedactorTests
	{
		private const string RedactedValue = "***REDACTED***";

		[Test]
		public void RedactSensitiveFields_ShouldRedactPassword()
		{
			// Arrange
			var jsonWithPassword = @"{""username"":""john.doe"",""password"":""secret123""}";

			// Act
			var redacted = SensitiveDataRedactor.RedactSensitiveFields(jsonWithPassword);

			// Assert
			Assert.That(redacted, Does.Contain(RedactedValue), "Redacted output should contain redaction marker");
			Assert.That(redacted, Does.Not.Contain("secret123"), "Redacted output should not contain original password");
			Assert.That(redacted, Does.Not.Contain("john.doe"), "Redacted output should not contain username value");
		}

		[Test]
		public void RedactSensitiveFields_ShouldRedactTokenAndApiKey()
		{
			// Arrange
			var jsonWithToken = @"{""token"":""Bearer abc123"",""apikey"":""xyz789"",""data"":""safe data""}";

			// Act
			var redacted = SensitiveDataRedactor.RedactSensitiveFields(jsonWithToken);

			// Assert
			Assert.That(redacted, Does.Contain(RedactedValue), "Redacted output should contain redaction marker");
			Assert.That(redacted, Does.Not.Contain("Bearer abc123"), "Redacted output should not contain original token");
			Assert.That(redacted, Does.Not.Contain("xyz789"), "Redacted output should not contain original API key");
			Assert.That(redacted, Does.Contain("safe data"), "Redacted output should preserve non-sensitive data");
		}

		[Test]
		public void RedactSensitiveFields_ShouldRedactNestedSensitiveFields()
		{
			// Arrange
			var nestedJson = @"{""user"":{""username"":""jane"",""password"":""pass456""},""sessionToken"":""token123""}";

			// Act
			var redacted = SensitiveDataRedactor.RedactSensitiveFields(nestedJson);

			// Assert
			Assert.That(redacted, Does.Contain(RedactedValue), "Redacted output should contain redaction marker");
			Assert.That(redacted, Does.Not.Contain("jane"), "Redacted output should not contain nested username");
			Assert.That(redacted, Does.Not.Contain("pass456"), "Redacted output should not contain nested password");
			Assert.That(redacted, Does.Not.Contain("token123"), "Redacted output should not contain session token");
		}

		[Test]
		public void RedactSensitiveFields_ShouldRedactJsonRpcRequest()
		{
			// Arrange
			var jsonRpcRequest = @"{""jsonrpc"":""2.0"",""method"":""authenticate"",""params"":{""username"":""admin"",""password"":""admin123""},""id"":1}";

			// Act
			var redacted = SensitiveDataRedactor.RedactSensitiveFields(jsonRpcRequest);

			// Assert
			Assert.That(redacted, Does.Contain(RedactedValue), "Redacted output should contain redaction marker");
			Assert.That(redacted, Does.Not.Contain("admin123"), "Redacted output should not contain password from params");
			Assert.That(redacted, Does.Not.Contain("admin"), "Redacted output should not contain username from params");
			Assert.That(redacted, Does.Contain("authenticate"), "Redacted output should preserve method name");
			Assert.That(redacted, Does.Contain("2.0"), "Redacted output should preserve jsonrpc version");
		}

		[Test]
		public void RedactSensitiveFields_ShouldHandleInvalidJson()
		{
			// Arrange
			var invalidJson = "not valid json {";

			// Act
			var redacted = SensitiveDataRedactor.RedactSensitiveFields(invalidJson);

			// Assert
			Assert.That(redacted, Is.Not.Null, "Should return non-null result for invalid JSON");
			Assert.That(redacted, Is.Not.Empty, "Should return non-empty result for invalid JSON");
			Assert.That(redacted, Does.Not.Contain("not valid json"), "Should not contain original invalid JSON content");
		}

		[Test]
		public void RedactSensitiveFields_ShouldHandleEmptyString()
		{
			// Arrange
			var emptyJson = string.Empty;

			// Act
			var redacted = SensitiveDataRedactor.RedactSensitiveFields(emptyJson);

			// Assert
			Assert.That(redacted, Is.Empty, "Should return empty string for empty input");
		}

		[Test]
		public void RedactSensitiveFields_ShouldHandleNullString()
		{
			// Arrange
			string nullJson = null;

			// Act
			var redacted = SensitiveDataRedactor.RedactSensitiveFields(nullJson);

			// Assert
			Assert.That(redacted, Is.Empty, "Should return empty string for null input");
		}

		[Test]
		public void RedactSensitiveFields_ShouldPreserveNonSensitiveFields()
		{
			// Arrange
			var jsonWithMixedFields = @"{""id"":42,""name"":""John"",""email"":""john@example.com"",""status"":""active""}";

			// Act
			var redacted = SensitiveDataRedactor.RedactSensitiveFields(jsonWithMixedFields);

			// Assert
			Assert.That(redacted, Does.Contain(RedactedValue), "Should redact email field");
			Assert.That(redacted, Does.Not.Contain("john@example.com"), "Should not contain original email");
			Assert.That(redacted, Does.Contain("42"), "Should preserve id field");
			Assert.That(redacted, Does.Contain("John"), "Should preserve name field");
			Assert.That(redacted, Does.Contain("active"), "Should preserve status field");
		}

		[Test]
		public void RedactSensitiveFields_ShouldRedactArraysWithSensitiveData()
		{
			// Arrange
			var jsonWithArray = @"{""users"":[{""username"":""user1"",""password"":""pass1""},{""username"":""user2"",""password"":""pass2""}]}";

			// Act
			var redacted = SensitiveDataRedactor.RedactSensitiveFields(jsonWithArray);

			// Assert
			Assert.That(redacted, Does.Contain(RedactedValue), "Should redact sensitive fields in array");
			Assert.That(redacted, Does.Not.Contain("user1"), "Should not contain first username");
			Assert.That(redacted, Does.Not.Contain("user2"), "Should not contain second username");
			Assert.That(redacted, Does.Not.Contain("pass1"), "Should not contain first password");
			Assert.That(redacted, Does.Not.Contain("pass2"), "Should not contain second password");
		}

		[Test]
		public void RedactSensitiveFields_ShouldRedactCommonSensitiveFieldNames()
		{
			// Arrange
			var jsonWithVariousSensitiveFields = @"{
				""password"":""secret"",
				""token"":""token123"",
				""ssn"":""123-45-6789"",
				""apikey"":""key123"",
				""api_key"":""key456"",
				""secret"":""secret789"",
				""authorization"":""Bearer xyz"",
				""auth"":""auth123"",
				""credentials"":""creds"",
				""credit_card"":""4111111111111111"",
				""creditcard"":""4111111111111111"",
				""cvv"":""123"",
				""pin"":""1234""
			}";

			// Act
			var redacted = SensitiveDataRedactor.RedactSensitiveFields(jsonWithVariousSensitiveFields);

			// Assert
			Assert.That(redacted, Does.Contain(RedactedValue), "Should contain redaction marker");
			Assert.That(redacted, Does.Not.Contain("secret"), "Should not contain 'secret' value");
			Assert.That(redacted, Does.Not.Contain("token123"), "Should not contain token value");
			Assert.That(redacted, Does.Not.Contain("123-45-6789"), "Should not contain SSN");
			Assert.That(redacted, Does.Not.Contain("key123"), "Should not contain apikey value");
			Assert.That(redacted, Does.Not.Contain("key456"), "Should not contain api_key value");
			Assert.That(redacted, Does.Not.Contain("secret789"), "Should not contain secret value");
			Assert.That(redacted, Does.Not.Contain("Bearer xyz"), "Should not contain authorization value");
			Assert.That(redacted, Does.Not.Contain("auth123"), "Should not contain auth value");
			Assert.That(redacted, Does.Not.Contain("creds"), "Should not contain credentials value");
			Assert.That(redacted, Does.Not.Contain("4111111111111111"), "Should not contain credit card number");
			Assert.That(redacted, Does.Not.Contain("1234"), "Should not contain PIN");
		}

		[Test]
		public void RedactSensitiveFields_ShouldHandleCaseInsensitiveFieldNames()
		{
			// Arrange
			var jsonWithMixedCase = @"{""Password"":""secret"",""TOKEN"":""token123"",""ApiKey"":""key456""}";

			// Act
			var redacted = SensitiveDataRedactor.RedactSensitiveFields(jsonWithMixedCase);

			// Assert
			Assert.That(redacted, Does.Contain(RedactedValue), "Should redact case-insensitive field names");
			Assert.That(redacted, Does.Not.Contain("secret"), "Should not contain password value");
			Assert.That(redacted, Does.Not.Contain("token123"), "Should not contain token value");
			Assert.That(redacted, Does.Not.Contain("key456"), "Should not contain API key value");
		}
	}
}

