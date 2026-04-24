using FluentAssertions;
using NUnit.Framework;
using Resgrid.Config;

namespace Resgrid.Tests.Config
{
	[TestFixture]
	public class PaymentProviderConfigTests
	{
		[Test]
		public void should_trim_and_normalize_paddle_environment()
		{
			var originalIsTestMode = PaymentProviderConfig.IsTestMode;
			var originalProductionEnvironment = PaymentProviderConfig.PaddleProductionEnvironment;

			try
			{
				PaymentProviderConfig.IsTestMode = false;
				PaymentProviderConfig.PaddleProductionEnvironment = " Production ";

				PaymentProviderConfig.GetPaddleEnvironment().Should().Be("production");
				PaymentProviderConfig.IsValidPaddleEnvironment(" sandbox ").Should().BeTrue();
				PaymentProviderConfig.IsValidPaddleEnvironment("invalid").Should().BeFalse();
			}
			finally
			{
				PaymentProviderConfig.IsTestMode = originalIsTestMode;
				PaymentProviderConfig.PaddleProductionEnvironment = originalProductionEnvironment;
			}
		}

		[Test]
		public void should_trim_paddle_client_token()
		{
			var originalIsTestMode = PaymentProviderConfig.IsTestMode;
			var originalProductionClientToken = PaymentProviderConfig.PaddleProductionClientToken;

			try
			{
				PaymentProviderConfig.IsTestMode = false;
				PaymentProviderConfig.PaddleProductionClientToken = "  live_7d279f61a3499fed520f7cd8c08  ";

				PaymentProviderConfig.GetPaddleClientToken().Should().Be("live_7d279f61a3499fed520f7cd8c08");
			}
			finally
			{
				PaymentProviderConfig.IsTestMode = originalIsTestMode;
				PaymentProviderConfig.PaddleProductionClientToken = originalProductionClientToken;
			}
		}

		[TestCase("live_7d279f61a3499fed520f7cd8c08", true)]
		[TestCase("test_4s7gd50ap72ms92nnsa20ma61lt", true)]
		[TestCase(" paddletoken_live_1940dc25e601d953fce733eeddfAty ", false)]
		[TestCase("live_7d279f61a3499fed520f7cd8c08/", false)]
		[TestCase("", false)]
		public void should_validate_documented_paddle_client_token_format(string token, bool expectedResult)
		{
			PaymentProviderConfig.IsValidPaddleClientToken(token).Should().Be(expectedResult);
		}
	}
}
