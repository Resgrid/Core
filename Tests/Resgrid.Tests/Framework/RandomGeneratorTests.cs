using FluentAssertions;
using NUnit.Framework;
using Resgrid.Framework;

namespace Resgrid.Tests.Framework
{
    [TestFixture]
    public class RandomGeneratorTests
    {
        [Test]
        public void TestRandomNumberGeneration()
        {
            var number = RandomGenerator.GetRandomNumber(1, 100);
            number.Should().BeGreaterThanOrEqualTo(1);
            number.Should().BeLessThanOrEqualTo(100);
        }

        [Test]
        public void TestRandomCharacterGenerationSpace()
        {
            var randomChar = RandomGenerator.GetRandomCharacter(true, true, true, true);
            randomChar.Should().NotBeNull();
            randomChar.Should().Be(char.Parse(" "));
        }

        [Test]
        public void TestRandomCharacterGeneration()
        {
            var randomChar = RandomGenerator.GetRandomCharacter(false, false, false, false);
            randomChar.Should().NotBeNull();
        }

        [Test]
        public void TestRandomStringGeneration()
        {
            var randomChar = RandomGenerator.GenerateRandomString(4, 16, false, false, false, false, false, false, null);
            randomChar.Should().NotBeNull();
        }
    }
}
