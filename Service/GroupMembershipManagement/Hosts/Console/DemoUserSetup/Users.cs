// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Graph;
using System;
using System.Text;
using System.Threading.Tasks;
using Entities;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Diagnostics;
using System.IO;

namespace DemoUserSetup
{
    public class Users
    {
        private static readonly AzureADUser[] _testUsers = new AzureADUser[AppSettings.LoadAppSettings().UserCount];
        private readonly GraphServiceClient _graphServiceClient = null;
        private Dictionary<string, string> _userIds = new Dictionary<string, string>();
        public Users(GraphServiceClient graphServiceClient)
        {
            _graphServiceClient = graphServiceClient;
        }

        public async Task AddUsers()
        {
            await DeleteOldObjects("microsoft.graph.user");
            var users = await _graphServiceClient.Users.Request().Filter("startswith(MailNickname, 'testuser')").Select("OnPremisesImmutableId,DisplayName,Id").GetAsync();
            HandleExistingUsers(users.CurrentPage);
            while (users.NextPageRequest != null)
            {
                users = await users.NextPageRequest.GetAsync();
                HandleExistingUsers(users.CurrentPage);
            }

			try
			{
				Stopwatch timer = Stopwatch.StartNew();
				for (int i = 0; i < _testUsers.Length; i++)
				{
					if (_testUsers[i] == null)
						await AddNewUser(i);

					if (i % 500 == 0)
					{
						var rate = timer.ElapsedMilliseconds / 500.0;
						var millisecondsLeft = rate * (_testUsers.Length - i);
						Console.WriteLine($"Added {i}/{_testUsers.Length} users ({i * 100.0 / _testUsers.Length:0.00}%). ETA: {TimeSpan.FromMilliseconds(millisecondsLeft)}");
						timer.Restart();
					}
				}

				Console.WriteLine("Done!");
			}
			catch (ServiceException ex)
			{
				Console.WriteLine(ex);
			}


            var sw = new StreamWriter("memberids.csv", false);
            sw.WriteLine("PersonnelNumber,AzureObjectId");
            foreach (var user in _userIds)
            {
                sw.WriteLine($"{user.Key},{user.Value}");
            }
            sw.Flush();
            sw.Close();
		}

		private int _permanentlyDeleted = 0;
		private Task PermanentlyDeleteItems(IEnumerable<DirectoryObject> toDelete)
		{
			return Task.WhenAll(toDelete.Select(async obj =>
			{
				await _graphServiceClient.Directory.DeletedItems[obj.Id].Request().DeleteAsync();
				Interlocked.Increment(ref _permanentlyDeleted);
			}));
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
				Console.WriteLine($"Cleaned up {_permanentlyDeleted} deleted items so far.");
			}
		}

		private void HandleExistingUsers(IEnumerable<User> users)
		{
			foreach (var user in users)
			{
				var userNumber = int.Parse(user.DisplayName.Substring("Test User ".Length));
				if (userNumber < _testUsers.Length)
					_testUsers[userNumber] = ToEntity(user);
                _userIds.Add(user.OnPremisesImmutableId, user.Id);
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

            var graphUser = await _graphServiceClient.Users.Request().AddAsync(user);
            _testUsers[number] = ToEntity(graphUser);

            if (!_userIds.ContainsKey(csvUser.ImmutableId))
                _userIds.Add(csvUser.ImmutableId, graphUser.Id);
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
