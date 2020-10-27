// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Graph;
using System;
using System.Text;
using System.Threading.Tasks;
using Entities;
using System.Collections.Generic;

namespace DemoUserSetup
{
	public class Users
	{
		private static readonly AzureADUser[] _testUsers = new AzureADUser[AppSettings.LoadAppSettings().UserCount];
		private readonly GraphServiceClient _graphServiceClient = null;

		public Users(GraphServiceClient graphServiceClient)
		{
			_graphServiceClient = graphServiceClient;
		}
		public async Task addUsers()
		{
			await DeleteOldObjects("microsoft.graph.user");
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
			}
		}

		private async Task PermanentlyDeleteItems(IEnumerable<DirectoryObject> toDelete)
		{
			foreach (var obj in toDelete)
			{
				await _graphServiceClient.Directory.DeletedItems[obj.Id].Request().DeleteAsync();
			}
		}
		private async Task DeleteOldObjects(string type)
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
		private async Task AddNewUser(int number)
		{
			var csvUser = _preprodGraphUsers.GetMockUserInfo(number);
			var user = new User
			{
				DisplayName = $"Test User {number}",
				AccountEnabled = true,
				PasswordProfile = new PasswordProfile { Password = RandomString() },
				MailNickname = $"testuser{number}",
				UsageLocation = "US",
				UserPrincipalName = $"{csvUser.Alias}@{AppSettings.LoadAppSettings().TenantName}",
				OnPremisesImmutableId = csvUser.ImmutableId
			};

			_testUsers[number] = ToEntity(await _graphServiceClient.Users.Request().AddAsync(user));
		}

		private static AzureADUser ToEntity(User user)
		{
			return new AzureADUser() { ObjectId = Guid.Parse(user.Id) };
		}

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
	}
}
