using System;
using FluentAssertions;
using NUnit.Framework;
using Resgrid.Services;

namespace Resgrid.Tests.Services
{
	[TestFixture]
	public class UnitTrackingIdentifierServiceTests
	{
		private UnitTrackingIdentifierService _service;

		[SetUp]
		public void SetUp()
		{
			_service = new UnitTrackingIdentifierService();
		}

		[Test]
		public void Normalize_WhitespaceAndCase_ReturnsStableIdentifier()
		{
			var result = _service.Normalize("  imei-Abc123  ");

			result.Should().Be("IMEI-ABC123");
		}

		[Test]
		public void Mask_Identifier_RevealsOnlyLastFourCharacters()
		{
			var result = _service.Mask("123456789012345");

			result.Should().Be("***********2345");
		}

		[Test]
		public void Normalize_IdentifierOverMaximumLength_Throws()
		{
			Action act = () => _service.Normalize(new string('a', 129));

			act.Should().Throw<ArgumentOutOfRangeException>();
		}
	}
}
