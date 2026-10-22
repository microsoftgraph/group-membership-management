// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.WebApi.Validators;

namespace WebApi.Tests
{
    [TestClass]
    public class GroupIdentitySelectionValidatorTests
    {
        [TestMethod]
        [DataRow("[{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-4000-8000-000000000001\"}]")]
        [DataRow("[{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-4000-8000-000000000001\",\"identityTypes\":[\"users\"]}]")]
        [DataRow("[{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-4000-8000-000000000001\",\"identityTypes\":[\"agents\"]}]")]
        [DataRow("[{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-4000-8000-000000000001\",\"identityTypes\":[\"agents\",\"users\"]}]")]
        [DataRow("[{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-4000-8000-000000000001\",\"identityTypes\":[\"users\",\"agents\"]}]")]
        [DataRow("[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"users.Department = 'Sales'\"},\"identityTypes\":[\"users\"]}]")]
        public void Validate_AcceptsCurrentSelections(string query)
        {
            Assert.IsNull(GroupIdentitySelectionValidator.Validate(query));
        }

        [TestMethod]
        [DataRow("[{\"type\":\"SecurityGroup\",\"source\":[\"00000000-0000-4000-8000-000000000001\"],\"identityTypes\":[\"agents\"]}]")]
        [DataRow("[{\"type\":\"GroupMembership\",\"source\":[\"00000000-0000-4000-8000-000000000001\"],\"identityTypes\":[\"users\"]}]")]
        [DataRow("[{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-4000-8000-000000000001\",\"IdentityTypes\":[\"users\"]}]")]
        [DataRow("[{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-4000-8000-000000000001\",\"identityTypes\":[\"Users\"]}]")]
        [DataRow("[{\"type\":\"GroupMembership\",\"source\":\"not-a-guid\",\"identityTypes\":[\"users\"]}]")]
        [DataRow("[{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-4000-8000-00000000000A\",\"identityTypes\":[\"users\"]}]")]
        [DataRow("[{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-4000-8000-000000000001\\n\",\"identityTypes\":[\"users\"]}]")]
        [DataRow("[{\"type\":\"GroupOwnership\",\"source\":[\"GroupMembership\"],\"identityTypes\":[\"users\"]}]")]
        [DataRow("[{\"type\":\"PlaceMembership\",\"source\":\"building-1\",\"identityTypes\":[\"users\"]}]")]
        [DataRow("[{\"type\":\"TeamsChannelMembership\",\"source\":{\"objectId\":\"team-1\",\"channelId\":\"channel-1\"},\"identityTypes\":[\"users\"]}]")]
        [DataRow("[{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-4000-8000-000000000001\",\"identityTypes\":[]}]")]
        [DataRow("[{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-4000-8000-000000000001\",\"identityTypes\":null}]")]
        [DataRow("[{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-4000-8000-000000000001\",\"identityTypes\":\"users\"}]")]
        [DataRow("[{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-4000-8000-000000000001\",\"identityTypes\":[1]}]")]
        [DataRow("[{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-4000-8000-000000000001\",\"identityTypes\":[\"devices\"]}]")]
        [DataRow("[{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-4000-8000-000000000001\",\"identityTypes\":[\"alternates\"]}]")]
        [DataRow("[{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-4000-8000-000000000001\",\"identityTypes\":[\"users\",\"users\"]}]")]
        [DataRow("[{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-4000-8000-000000000001\",\"identityTypes\":[\"users\"],\"identityTypes\":[\"agents\"]}]")]
        [DataRow("[{\"type\":\"GroupMembership\",\"source\":{\"identityTypes\":[\"users\"]}}]")]
        [DataRow("[{\"type\":\"GroupMembership\",\"source\":\"00000000-0000-4000-8000-000000000001\",\"identityTypes\":[\"users\"]},{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"users.Department = 'Sales'\"},\"identityTypes\":[\"users\"]},{\"type\":\"GroupMembership\",\"source\":\"not-a-guid\",\"identityTypes\":[\"agents\"]}]")]
        public void Validate_RejectsSelectionsOutsideTheSupportedGroupShape(string query)
        {
            var error = GroupIdentitySelectionValidator.Validate(query);

            Assert.IsNotNull(error);
            Assert.AreEqual("InvalidGroupIdentitySelection", error.ErrorCode);
        }

        [TestMethod]
        [DataRow("[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"users.Department = 'Sales'\"},\"IdentityTypes\":[\"users\"]}]")]
        [DataRow("[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"users.Department = 'Sales'\",\"identityTypes\":[\"agents\"]}}] ")]
        [DataRow("[{\"type\":\"SqlMembership\",\"source\":{\"filter\":\"users.Department = 'Sales'\"},\"identityTypes\":[\"users\"],\"identityTypes\":[\"agents\"]}]")]
        public void Validate_DoesNotApplyGroupRulesToSqlMembership(string query)
        {
            Assert.IsNull(GroupIdentitySelectionValidator.Validate(query));
        }

        [TestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("not-json")]
        [DataRow("{}")]
        public void Validate_LeavesUnrelatedQueryErrorsToExistingValidators(string? query)
        {
            Assert.IsNull(GroupIdentitySelectionValidator.Validate(query));
        }
    }
}
