using System;
using System.Text.Json;
using Resgrid.Web.Mcp.Infrastructure;

namespace Resgrid.Web.Mcp.Tests
{
	/// <summary>
	/// Simple test examples for SensitiveDataRedactor
	/// </summary>
	public static class SensitiveDataRedactorTests
	{
		public static void RunTests()
		{
			Console.WriteLine("Testing SensitiveDataRedactor...\n");

			// Test 1: Redact password
			var jsonWithPassword = @"{""username"":""john.doe"",""password"":""secret123""}";
			var redacted1 = SensitiveDataRedactor.RedactSensitiveFields(jsonWithPassword);
			Console.WriteLine($"Original: {jsonWithPassword}");
			Console.WriteLine($"Redacted: {redacted1}\n");

			// Test 2: Redact token and apikey
			var jsonWithToken = @"{""token"":""Bearer abc123"",""apikey"":""xyz789"",""data"":""safe data""}";
			var redacted2 = SensitiveDataRedactor.RedactSensitiveFields(jsonWithToken);
			Console.WriteLine($"Original: {jsonWithToken}");
			Console.WriteLine($"Redacted: {redacted2}\n");

			// Test 3: Nested JSON with sensitive fields
			var nestedJson = @"{""user"":{""username"":""jane"",""password"":""pass456""},""sessionToken"":""token123""}";
			var redacted3 = SensitiveDataRedactor.RedactSensitiveFields(nestedJson);
			Console.WriteLine($"Original: {nestedJson}");
			Console.WriteLine($"Redacted: {redacted3}\n");

			// Test 4: JSON-RPC request with params containing sensitive data
			var jsonRpcRequest = @"{""jsonrpc"":""2.0"",""method"":""authenticate"",""params"":{""username"":""admin"",""password"":""admin123""},""id"":1}";
			var redacted4 = SensitiveDataRedactor.RedactSensitiveFields(jsonRpcRequest);
			Console.WriteLine($"Original: {jsonRpcRequest}");
			Console.WriteLine($"Redacted: {redacted4}\n");

			// Test 5: Invalid JSON (should return metadata only)
			var invalidJson = "not valid json {";
			var redacted5 = SensitiveDataRedactor.RedactSensitiveFields(invalidJson);
			Console.WriteLine($"Original: {invalidJson}");
			Console.WriteLine($"Redacted: {redacted5}\n");

			Console.WriteLine("All tests completed!");
		}
	}
}

