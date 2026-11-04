using System.Globalization;
using NUnit.Framework;
using PudgeWars.Net;

namespace PudgeWars.Tests
{
    public class ServerOptionsTests
    {
        [Test]
        public void DefaultsMatchTheNormalLobby()
        {
            Assert.IsTrue(ServerOptions.TryParse(new[] { "-batchmode", "-nographics", "-logFile", "-" }, out var options, out var error), error);
            Assert.AreEqual(7777, options.Port);
            Assert.AreEqual(3, options.TeamSize);
            Assert.IsTrue(options.Bots);
            Assert.AreEqual(1f, options.BotDifficulty);
        }

        [Test]
        public void SettingsUseDecimalPointUnderRussianLocale()
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
                Assert.IsTrue(ServerOptions.TryParse(new[] { "-port", "8888", "-teamSize", "5", "-bots", "0",
                    "-botDifficulty", "1.75" }, out var options, out var error), error);
                Assert.AreEqual(8888, options.Port);
                Assert.AreEqual(5, options.TeamSize);
                Assert.IsFalse(options.Bots);
                Assert.AreEqual(1.75f, options.BotDifficulty);
            }
            finally { CultureInfo.CurrentCulture = previous; }
        }

        [TestCase("-port", "0")]
        [TestCase("-port", "65536")]
        [TestCase("-port", "-1")]
        [TestCase("-teamSize", "0")]
        [TestCase("-teamSize", "6")]
        [TestCase("-bots", "true")]
        [TestCase("-botDifficulty", "NaN")]
        [TestCase("-botDifficulty", "Infinity")]
        [TestCase("-botDifficulty", "0")]
        [TestCase("-botDifficulty", "4")]
        [TestCase("-botDifficulty", "1,5")]
        public void InvalidSettingsFailBeforeListening(string flag, string value)
        {
            Assert.IsFalse(ServerOptions.TryParse(new[] { flag, value }, out _, out var error));
            StringAssert.Contains(flag, error);
        }

        [TestCase("-port")]
        [TestCase("-teamSize")]
        [TestCase("-bots")]
        [TestCase("-botDifficulty")]
        public void MissingValueFails(string flag)
        {
            Assert.IsFalse(ServerOptions.TryParse(new[] { flag }, out _, out var error));
            StringAssert.Contains(flag, error);
        }

        [Test]
        public void MissingValueBeforeAnotherFlagFails()
        {
            Assert.IsFalse(ServerOptions.TryParse(new[] { "-port", "-bots", "0" }, out _, out var error));
            StringAssert.Contains("-port", error);
        }
    }
}
