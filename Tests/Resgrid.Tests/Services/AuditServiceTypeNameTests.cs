using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Model;
using Resgrid.Services;
using File = System.IO.File;

namespace Resgrid.Tests.Services
{
	/// <summary>
	/// AuditService.GetAuditLogTypeString names each audit type in English. The worker and the moderation services
	/// store that text in audit messages, and the audit log pages fall back to it for types without a Security
	/// resource, so every defined type needs a real name and it must agree with Security.en.resx.
	/// </summary>
	[TestFixture]
	public class AuditServiceTypeNameTests
	{
		private readonly AuditService _service = new AuditService(null, null);

		[Test]
		public void Every_audit_type_has_a_real_name()
		{
			var unnamed = Enum.GetValues<AuditLogTypes>()
				.Where(type => _service.GetAuditLogTypeString(type).StartsWith("Unknown (", StringComparison.Ordinal))
				.ToList();

			unnamed.Should().BeEmpty("a new AuditLogTypes value needs a case in AuditService.GetAuditLogTypeString");
		}

		[Test]
		public void Undefined_audit_type_values_are_still_reported_as_unknown()
		{
			_service.GetAuditLogTypeString((AuditLogTypes)999999).Should().Be("Unknown (999999)");
		}

		[Test]
		public void English_security_resources_use_the_same_audit_type_names()
		{
			var english = XDocument.Load(Path.Combine(RepositoryRoot(), "Core", "Resgrid.Localization", "Areas", "User", "Security", "Security.en.resx"))
				.Root!.Elements("data")
				.ToDictionary(e => (string)e.Attribute("name")!, e => (string)e.Element("value") ?? string.Empty, StringComparer.Ordinal);

			var mismatches = new List<string>();
			foreach (var type in Enum.GetValues<AuditLogTypes>())
			{
				if (english.TryGetValue("AuditLogType" + type, out var resourceName) && resourceName != _service.GetAuditLogTypeString(type))
					mismatches.Add($"{type}: resx \"{resourceName}\" vs service \"{_service.GetAuditLogTypeString(type)}\"");
			}

			mismatches.Should().BeEmpty("the audit log pages show the resx name, and stored messages use the service name");
		}

		private static string RepositoryRoot()
		{
			var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
			while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Resgrid.sln"))) directory = directory.Parent;
			return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root unavailable.");
		}
	}
}
