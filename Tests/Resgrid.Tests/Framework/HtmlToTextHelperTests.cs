using FluentAssertions;
using NUnit.Framework;
using Resgrid.Framework;

namespace Resgrid.Tests.Framework
{
    [TestFixture]
    public class HtmlToTextHelperTests
    {
        [Test]
        public void TestConvertHtml()
        {
            var testHtml = "<h1 style=margin-top:0px;margin-bottom:0px;padding:0px;'>Just testing to see if the text message section of resgrid works</h1>";
            var test = HtmlToTextHelper.ConvertHtml(testHtml);

            test.Should().Be("Just testing to see if the text message section of resgrid works");
        }
    }
}