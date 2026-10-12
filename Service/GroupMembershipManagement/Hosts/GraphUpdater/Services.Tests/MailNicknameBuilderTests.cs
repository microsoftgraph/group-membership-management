// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Repositories.GraphGroups;
using System.Linq;

namespace Services.Tests
{
    [TestClass]
    public class MailNicknameBuilderTests
    {
        [TestMethod]
        public void KeepsAnAliasMicrosoftGraphAccepts()
        {
            Assert.AreEqual("finance-team_1.0", MailNicknameBuilder.Build("finance-team_1.0", "Finance Team"));
        }

        [TestMethod]
        [DataRow("finance+ops")]
        [DataRow("finance!#$%&'*+-/=?^_`{|}~ops")]
        public void KeepsSupportedAliasPunctuation(string alias)
        {
            Assert.AreEqual(alias, MailNicknameBuilder.Build(alias, "Finance Team"));
        }

        [TestMethod]
        public void KeepsExactlyTheSupportedPrintableAsciiCharacters()
        {
            const string rejectedCharacters = "@()\\[]\";:<>,";
            for (var code = 0; code <= 127; code++)
            {
                var character = (char)code;
                var expected = code >= 33 && code <= 126 && rejectedCharacters.IndexOf(character) < 0
                    ? $"finance{character}ops"
                    : "financeops";

                Assert.AreEqual(expected, MailNicknameBuilder.Build($"finance{character}ops", "Finance Team"),
                    $"Unexpected handling of ASCII character {code}.");
            }
        }

        [TestMethod]
        [DataRow("finance team", "financeteam")]
        [DataRow("finance@team", "financeteam")]
        [DataRow("finance(team)", "financeteam")]
        [DataRow("finance[team]", "financeteam")]
        [DataRow("finance;team:a<b>c,d", "financeteamabcd")]
        [DataRow("finance\"team\\x", "financeteamx")]
        [DataRow("finance\t\r\nteam\u007f", "financeteam")]
        [DataRow("finance\u00e9\u200bteam", "financeteam")]
        public void StripsCharactersMicrosoftGraphRejects(string alias, string expected)
        {
            Assert.AreEqual(expected, MailNicknameBuilder.Build(alias, "Finance Team"));
        }

        [TestMethod]
        public void TruncatesToTheGraphLimit()
        {
            var alias = new string('a', 200);

            var nickname = MailNicknameBuilder.Build(alias, "Finance Team");

            Assert.AreEqual(MailNicknameBuilder.MaxLength, nickname.Length);
        }

        [TestMethod]
        public void TrimsLeadingAndTrailingPeriods()
        {
            Assert.AreEqual("finance.team", MailNicknameBuilder.Build("..finance.team..", "Finance Team"));
        }

        [TestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        [DataRow("@()")]
        public void FallsBackToTheDisplayNameWhenTheAliasIsUnusable(string alias)
        {
            var nickname = MailNicknameBuilder.Build(alias, "Finance Team");

            StringAssert.StartsWith(nickname, "FinanceTeam-");
            Assert.AreEqual("FinanceTeam-".Length + 8, nickname.Length);
        }

        [TestMethod]
        public void FallsBackToAConstantPrefixWhenTheDisplayNameIsAlsoUnusable()
        {
            var nickname = MailNicknameBuilder.Build(null, "@()");

            StringAssert.StartsWith(nickname, "gmm-group-");
            Assert.AreEqual("gmm-group-".Length + 8, nickname.Length);
        }

        [TestMethod]
        public void KeepsTheFallbackWithinTheGraphLimit()
        {
            var nickname = MailNicknameBuilder.Build(null, new string('a', 200));

            Assert.IsTrue(nickname.Length <= MailNicknameBuilder.MaxLength, $"Length was {nickname.Length}.");
        }

        [TestMethod]
        public void ProducesADistinctFallbackEachTime()
        {
            var nicknames = Enumerable.Range(0, 25)
                                      .Select(_ => MailNicknameBuilder.Build(null, "Finance Team"))
                                      .Distinct()
                                      .Count();

            Assert.AreEqual(25, nicknames);
        }
    }
}
