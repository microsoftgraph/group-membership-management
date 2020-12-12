using Entities;
using Microsoft.Graph;
using Microsoft.Graph.Auth;
using Microsoft.Identity.Client;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Repositories.Contracts;
using Repositories.GraphGroups;
using Repositories.MembershipDifference;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Repositories.Integration.Tests
{
	[TestClass]
	public class GraphGroupRepositoryTests
	{
		// an Azure AD app in your demo tenant with http://localhost as a redirect URI under "Mobile and Desktop Applications"
		// (it won't work if it's under Web, which is the default!)
		// and the following delegated permissions:
		// - Group.ReadWrite.All
		// - User.ReadWrite.All
		// When you run this, sign in with your demo tenant administrator credentials.

		const string ClientId = "2ac03521-fa6c-48c4-bf03-033eb930df5e";

		// Your demo tenant ID and name
		const string Tenant = "03d91f7c-fb5e-466e-a6ea-392c0e965d46";
		const string TenantName = "M365x455139.onmicrosoft.com";

		// Basically, these tests won't run correctly in source control because they don't have the proper credentials
		// if you're running these on your local machine, change this to true.
		const bool RunTests = false;

		private static GraphGroupRepository _groupRepo;
		private static GraphServiceClient _graphServiceClient;

		private AzureADGroup _sourceGroup;
		private AzureADGroup _destinationGroup;

		private List<AzureADGroup> _groupsToRemove;

		// make it a weird number to make sure the code handles that okay
		// you can have at most 50,000 things in a directory.
		// that number can be raised, but it's 50k for *everything* by default
		// users, groups, and whatnot all count towards that
		// I got 37,125 users in my testing, but we want to leave some breathing room for groups and such
		// https://docs.microsoft.com/en-us/azure/active-directory/users-groups-roles/directory-service-limits-restrictions
		const int UserCount = 20977;
		private static readonly AzureADUser[] _testUsers = new AzureADUser[UserCount];

		private static readonly TimeSpan _waitForGraph = TimeSpan.FromSeconds(30);

		[TestMethod]
		public async Task CanCheckIfGroupExists()
		{
			foreach (var group in _groupsToRemove)
				Assert.IsTrue(await _groupRepo.GroupExists(group.ObjectId));
			Assert.IsFalse(await _groupRepo.GroupExists(Guid.Empty));
			Assert.IsFalse(await _groupRepo.GroupExists(Guid.NewGuid()));
		}

		[TestMethod]
		public async Task CanAddAndRemoveUsersFromGroup()
		{
			await _groupRepo.AddUsersToGroup(_testUsers, _sourceGroup);

			await Task.Delay(_waitForGraph); // Sometimes you have to wait for the graph to catch up.

			var groupMembers = await _groupRepo.GetUsersInGroupTransitively(_sourceGroup.ObjectId);

			Assert.AreEqual(UserCount, _testUsers.Length);
			Assert.IsTrue(SequencesMatch(_testUsers, groupMembers));

			await _groupRepo.RemoveUsersFromGroup(_testUsers, _sourceGroup);

			await Task.Delay(_waitForGraph); // Sometimes you have to wait for the graph to catch up.

			groupMembers = await _groupRepo.GetUsersInGroupTransitively(_sourceGroup.ObjectId);

			Assert.AreEqual(0, groupMembers.Count);
		}

		[TestMethod]
		public async Task CanGroupSyncIntoEmptyGroup()
		{
			await _groupRepo.AddUsersToGroup(_testUsers, _sourceGroup);

			await Task.Delay(_waitForGraph); // Sometimes you have to wait for the graph to catch up.

			var sourceGroupMembers = await _groupRepo.GetUsersInGroupTransitively(_sourceGroup.ObjectId);

			Assert.AreEqual(UserCount, _testUsers.Length);
			Assert.IsTrue(SequencesMatch(_testUsers, sourceGroupMembers));

			var destinationGroupMembers = await _groupRepo.GetUsersInGroupTransitively(_destinationGroup.ObjectId);

			Assert.AreEqual(0, destinationGroupMembers.Count);

			var calc = new MembershipDifferenceCalculator<AzureADUser>();
			var membershipDelta = calc.CalculateDifference(sourceGroupMembers, destinationGroupMembers);

			Assert.IsTrue(SequencesMatch(_testUsers, membershipDelta.ToAdd));
			Assert.AreEqual(0, membershipDelta.ToRemove.Count);

			await _groupRepo.AddUsersToGroup(membershipDelta.ToAdd, _destinationGroup);
			await _groupRepo.RemoveUsersFromGroup(membershipDelta.ToRemove, _destinationGroup);

			await Task.Delay(_waitForGraph);

			destinationGroupMembers = await _groupRepo.GetUsersInGroupTransitively(_destinationGroup.ObjectId);

			Assert.IsTrue(SequencesMatch(_testUsers, destinationGroupMembers));
		}

		[TestMethod]
		public async Task CanGroupSyncIntoNonEmptyGroup()
		{
			var initialSourceUsers = _testUsers.Where((_, idx) => idx % 2 == 0).ToArray();
			var inititalDestinationUsers = _testUsers.Where((_, idx) => idx % 2 == 1).ToArray();

			await _groupRepo.AddUsersToGroup(initialSourceUsers, _sourceGroup);
			await _groupRepo.AddUsersToGroup(inititalDestinationUsers, _destinationGroup);

			await Task.Delay(_waitForGraph); // Sometimes you have to wait for the graph to catch up.

			var sourceGroupMembers = await _groupRepo.GetUsersInGroupTransitively(_sourceGroup.ObjectId);

			Assert.IsTrue(SequencesMatch(initialSourceUsers, sourceGroupMembers));

			var destinationGroupMembers = await _groupRepo.GetUsersInGroupTransitively(_destinationGroup.ObjectId);

			Assert.IsTrue(SequencesMatch(inititalDestinationUsers, destinationGroupMembers));

			var calc = new MembershipDifferenceCalculator<AzureADUser>();
			var membershipDelta = calc.CalculateDifference(sourceGroupMembers, destinationGroupMembers);

			Assert.IsTrue(SequencesMatch(initialSourceUsers, membershipDelta.ToAdd));
			Assert.IsTrue(SequencesMatch(inititalDestinationUsers, membershipDelta.ToRemove));

			await _groupRepo.AddUsersToGroup(membershipDelta.ToAdd, _destinationGroup);
			await _groupRepo.RemoveUsersFromGroup(membershipDelta.ToRemove, _destinationGroup);

			await Task.Delay(_waitForGraph); // Sometimes you have to wait for the graph to catch up.

			destinationGroupMembers = await _groupRepo.GetUsersInGroupTransitively(_destinationGroup.ObjectId);

			Assert.IsTrue(SequencesMatch(initialSourceUsers, destinationGroupMembers));
		}

		[TestMethod]
		public async Task CanGroupSyncIntoOverlappingGroup()
		{
			// the bottom half is in the source
			// the upper two thirds are in the destination
			var initialSourceUsers = _testUsers.Where((_, idx) => idx < (UserCount / 2)).ToArray();
			var initialDestinationUsers = _testUsers.Where((_, idx) => idx > (UserCount / 3)).ToArray();

			await _groupRepo.AddUsersToGroup(initialSourceUsers, _sourceGroup);
			await _groupRepo.AddUsersToGroup(initialDestinationUsers, _destinationGroup);

			await Task.Delay(_waitForGraph); // Sometimes you have to wait for the graph to catch up.

			var sourceGroupMembers = await _groupRepo.GetUsersInGroupTransitively(_sourceGroup.ObjectId);

			Assert.IsTrue(SequencesMatch(initialSourceUsers, sourceGroupMembers));

			var destinationGroupMembers = await _groupRepo.GetUsersInGroupTransitively(_destinationGroup.ObjectId);

			Assert.IsTrue(SequencesMatch(initialDestinationUsers, destinationGroupMembers));

			var calc = new MembershipDifferenceCalculator<AzureADUser>();
			var membershipDelta = calc.CalculateDifference(sourceGroupMembers, destinationGroupMembers);

			// Have to add the bottom third and remove the top half
			Assert.IsTrue(SequencesMatch(_testUsers.Where((_, idx) => idx <= (UserCount / 3)), membershipDelta.ToAdd));
			Assert.IsTrue(SequencesMatch(_testUsers.Where((_, idx) => idx >= (UserCount / 2)), membershipDelta.ToRemove));

			await _groupRepo.AddUsersToGroup(membershipDelta.ToAdd, _destinationGroup);
			await _groupRepo.RemoveUsersFromGroup(membershipDelta.ToRemove, _destinationGroup);

			await Task.Delay(_waitForGraph); // Sometimes you have to wait for the graph to catch up.

			destinationGroupMembers = await _groupRepo.GetUsersInGroupTransitively(_destinationGroup.ObjectId);

			Assert.IsTrue(SequencesMatch(initialSourceUsers, destinationGroupMembers));
		}

		[TestMethod]
		public async Task CanGroupSyncWithNestedGroups()
		{
			// the bottom half is in the source
			// the upper two thirds are in the destination
			var initialSourceUsers = _testUsers.Where((_, idx) => idx < (UserCount / 2)).ToArray();
			var initialDestinationUsers = _testUsers.Where((_, idx) => idx > (UserCount / 3)).ToArray();

			await _groupRepo.AddUsersToGroup(initialDestinationUsers, _destinationGroup);

			const int testGroupCount = 5;
			var testGroups = new AzureADGroup[testGroupCount];
			for (int i = 0; i < testGroupCount; i++)
			{
				testGroups[i] = await CreateOrClearGroup(
					new Group { DisplayName = $"TestSourceGroup{i}", MailEnabled = false, MailNickname = $"testsourcegroup{i}", SecurityEnabled = true });

				_groupsToRemove.Add(testGroups[i]);

				// add all the users to the first group, every other user to the second group, etc.
				await _groupRepo.AddUsersToGroup(initialSourceUsers.Where((_, idx) => idx % (i + 1) == 0), testGroups[i]);
				await _graphServiceClient.Groups[_sourceGroup.ObjectId.ToString()].Members.References.Request().AddAsync(new DirectoryObject { Id = testGroups[i].ObjectId.ToString() });
			}

			await Task.Delay(_waitForGraph); // Sometimes you have to wait for the graph to catch up.

			var sourceGroupMembers = await _groupRepo.GetUsersInGroupTransitively(_sourceGroup.ObjectId);

			Assert.IsTrue(SequencesMatch(initialSourceUsers, sourceGroupMembers));

			var destinationGroupMembers = await _groupRepo.GetUsersInGroupTransitively(_destinationGroup.ObjectId);

			Assert.IsTrue(SequencesMatch(initialDestinationUsers, destinationGroupMembers));

			var calc = new MembershipDifferenceCalculator<AzureADUser>();
			var membershipDelta = calc.CalculateDifference(sourceGroupMembers, destinationGroupMembers);

			// Have to add the bottom third and remove the top half
			Assert.IsTrue(SequencesMatch(_testUsers.Where((_, idx) => idx <= (UserCount / 3)), membershipDelta.ToAdd));
			Assert.IsTrue(SequencesMatch(_testUsers.Where((_, idx) => idx >= (UserCount / 2)), membershipDelta.ToRemove));

			await _groupRepo.AddUsersToGroup(membershipDelta.ToAdd, _destinationGroup);
			await _groupRepo.RemoveUsersFromGroup(membershipDelta.ToRemove, _destinationGroup);

			await Task.Delay(_waitForGraph); // Sometimes you have to wait for the graph to catch up.

			destinationGroupMembers = await _groupRepo.GetUsersInGroupTransitively(_destinationGroup.ObjectId);

			Assert.IsTrue(SequencesMatch(initialSourceUsers, destinationGroupMembers));
		}

		private static bool SequencesMatch(IEnumerable<AzureADUser> expected, IEnumerable<AzureADUser> actual)
		{
			return expected.OrderBy(x => x.ObjectId).SequenceEqual(actual.OrderBy(x => x.ObjectId));
		}

		[ClassInitialize]
		public static async Task CreateGraphRepository(TestContext _)
		{
			if (!RunTests)
				Assert.Inconclusive("Tests not run because I assume this is in contiguous integration. Set RunTests to true if this is not the case.");

			var publicClientApp = PublicClientApplicationBuilder.Create(ClientId)
			.WithRedirectUri("http://localhost")
			.WithAuthority(AzureCloudInstance.AzurePublic, Tenant)
			.Build();

			var authProvider = new InteractiveAuthenticationProvider(publicClientApp);
			_groupRepo = new GraphGroupRepository(authProvider, new MockLogger());
			_graphServiceClient = new GraphServiceClient(authProvider);

			await DeleteOldObjects("microsoft.graph.group");
			await DeleteOldObjects("microsoft.graph.user");

			// not the most efficient, but writing all the batching logic to add a bunch of users is a pain
			// we don't delete the users after, so this only has to be done once anyways.
			var users = await _graphServiceClient.Users.Request().Filter("startswith(MailNickname, 'testuser')").GetAsync();
			HandleExistingUsers(users.CurrentPage);
			while (users.NextPageRequest != null)
			{
				users = await users.NextPageRequest.GetAsync();
				HandleExistingUsers(users.CurrentPage);
			}

			try
			{
				for (int i = 0; i < _testUsers.Length; i++)
				{
					if (_testUsers[i] == null)
						await AddNewUser(i);
				}
			}
			catch (ServiceException ex)
			{
				Assert.Fail("Failed creating user. This happens sometimes. Please run the tests again and it'll pick up where it left off. Exception: {0}", ex);
			}
		}

		// Basically, when you delete a group (or a user), it actually goes to this recycle bin-like deleted items area.
		// go ahead and clean those out.
		private static async Task DeleteOldObjects(string type)
		{
			var deletedItemsUrl = _graphServiceClient.Directory.DeletedItems.AppendSegmentToRequestUrl(type);
			var builder = new DirectoryDeletedItemsCollectionRequestBuilder(deletedItemsUrl, _graphServiceClient);
			var deletedItems = await builder.Request().GetAsync();

			await PermanentlyDeleteItems(deletedItems);

			while (deletedItems.NextPageRequest != null)
			{
				deletedItems = await deletedItems.NextPageRequest.GetAsync();
				await PermanentlyDeleteItems(deletedItems);
			}
		}

		private static async Task PermanentlyDeleteItems(IEnumerable<DirectoryObject> toDelete)
		{
			foreach (var obj in toDelete)
			{
				await _graphServiceClient.Directory.DeletedItems[obj.Id].Request().DeleteAsync();
			}
		}

		[TestInitialize]
		public async Task CreateGroupsAndUsers()
		{
			if (!RunTests)
				Assert.Inconclusive("Tests not run because I assume this is in contiguous integration. Set RunTests to true if this is not the case.");

			_sourceGroup = await CreateOrClearGroup(
				new Group { DisplayName = "TestSourceGroup", MailEnabled = false, MailNickname = "testsourcegroup", SecurityEnabled = true });

			// Can't use the API to create mail-enabled groups right now, apparently.
			_destinationGroup = await CreateOrClearGroup(
				new Group { DisplayName = "TestDestinationGroup", MailEnabled = false, MailNickname = "testdestgroup", SecurityEnabled = true });

			_groupsToRemove = new List<AzureADGroup> { _sourceGroup, _destinationGroup };
		}

		private static void HandleExistingUsers(IEnumerable<User> users)
		{
			foreach (var user in users)
			{
				var userNumber = int.Parse(user.DisplayName.Substring("Test User ".Length));
				if (userNumber < _testUsers.Length)
					_testUsers[userNumber] = ToEntity(user);
			}
		}

		private static readonly DemoData _preprodGraphUsers = new DemoData();
		private static async Task AddNewUser(int number)
		{
			var csvUser = _preprodGraphUsers.GetMockUserInfo(number);
			var user = new User
			{
				DisplayName = $"Test User {number}",
				AccountEnabled = true,
				PasswordProfile = new PasswordProfile { Password = RandomString() },
				MailNickname = $"testuser{number}",
				UserPrincipalName = $"{csvUser.Alias}@{TenantName}",
				OnPremisesImmutableId = csvUser.ImmutableId
			};

			_testUsers[number] = ToEntity(await _graphServiceClient.Users.Request().AddAsync(user));
		}

		private static async Task<AzureADGroup> CreateOrClearGroup(Group group)
		{
			var response = await _graphServiceClient.Groups.Request().Filter($"MailNickname eq '{group.MailNickname}'").GetAsync();

			// I'm pretty sure this will only ever have one thing in it- mail nicknames have to be unique- but can't hurt to iterate
			// Deleting and recreating the group is faster than removing all its users.
			foreach (var existingGroup in response.CurrentPage)
			{
				await _graphServiceClient.Groups[existingGroup.Id].Request().DeleteAsync();
			}

			return ToEntity(await _graphServiceClient.Groups.Request().AddAsync(group));
		}

		[TestCleanup]
		public async Task RemoveGroups()
		{
			// there should be no way this can be null, but here we are
			if (_groupsToRemove == null) { return; }
			foreach (var group in _groupsToRemove)
			{
				await _graphServiceClient.Groups[group.ObjectId.ToString()].Request().DeleteAsync();
			}
		}

		private static AzureADUser ToEntity(User user)
		{
			return new AzureADUser() { ObjectId = Guid.Parse(user.Id) };
		}

		private static AzureADGroup ToEntity(Group group)
		{
			return new AzureADGroup() { ObjectId = Guid.Parse(group.Id) };
		}


		// this is absolutely overkill, but it'll quiet that credscan warning
		// if you're gonna do something, do it right
		// based on the guidelines here: https://docs.microsoft.com/en-us/azure/active-directory/authentication/concept-sspr-policy#password-policies-that-only-apply-to-cloud-user-accounts
		private static string RandomString()
		{
			StringBuilder sb = new StringBuilder();
			for (int i = 0; i < 8; i++)
				sb.Append(CharacterBetween('A', 'Z'));
			for (int i = 0; i < 8; i++)
				sb.Append(CharacterBetween('a', 'z'));
			for (int i = 0; i < 8; i++)
				sb.Append(CharacterBetween('0', '9'));
			for (int i = 0; i < 8; i++)
				sb.Append(CharacterIn("@#$%^&*-_!+=[]{}| \\:',.?/`~\"();"));
			Shuffle(sb);
			return sb.ToString();
		}

		private static readonly Random _random = new Random();
		private static char CharacterBetween(char begin, char end)
		{
			return (char)_random.Next(begin, end + 1);
		}

		private static char CharacterIn(string str)
		{
			return str[_random.Next(0, str.Length)];
		}

		private static void Shuffle(StringBuilder sb)
		{
			for (int i = 0; i < sb.Length; i++)
			{
				int toswap = _random.Next(i, sb.Length);
				char temp = sb[i];
				sb[i] = sb[toswap];
				sb[toswap] = temp;
			}
		}

		private class MockLogger : ILoggingRepository
		{
			public string SyncJobInfo { get; set; }

			public Task LogMessageAsync(LogMessage logMessage)
			{
				return Task.CompletedTask;
			}
		}

	}
}
