// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Microsoft.Graph;
using System;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Diagnostics;
using System.IO;
using Models;
using Microsoft.Graph.Models;
using Microsoft.Kiota.Abstractions;
using Microsoft.Graph.Models.ODataErrors;

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
            var usersResponse = await _graphServiceClient
                                .Users
                                .GetAsync(requestConfiguration =>
                                {
                                    requestConfiguration.QueryParameters.Filter = "startswith(MailNickname, 'testuser')";
                                    requestConfiguration.QueryParameters.Select = new[] { "OnPremisesImmutableId", "DisplayName", "Id" };
                                });

            HandleExistingUsers(usersResponse.Value);
            while (usersResponse.OdataNextLink != null)
            {
                var nextPageRequest = new RequestInformation
                {
                    HttpMethod = Method.GET,
                    UrlTemplate = usersResponse.OdataNextLink
                };

                usersResponse = await _graphServiceClient
                                                .RequestAdapter
                                                .SendAsync(nextPageRequest,
                                                           UserCollectionResponse.CreateFromDiscriminatorValue);


                HandleExistingUsers(usersResponse.Value);
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
            catch (ODataError ex)
            {
                Console.WriteLine(ex);
            }


            using var sw = new StreamWriter("memberids.csv", false);
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
                await _graphServiceClient.Directory.DeletedItems[obj.Id].DeleteAsync();
                Interlocked.Increment(ref _permanentlyDeleted);
            }));
        }

        private async Task DeleteOldObjects(string type)
        {
            var deletedItems = await _graphServiceClient.Directory.DeletedItems.GraphUser.GetAsync();

            await PermanentlyDeleteItems(deletedItems.Value);

            while (deletedItems.OdataNextLink != null)
            {
                var nextPageRequest = new RequestInformation
                {
                    HttpMethod = Method.GET,
                    UrlTemplate = deletedItems.OdataNextLink
                };

                deletedItems = await _graphServiceClient
                                               .RequestAdapter
                                               .SendAsync(nextPageRequest,
                                                          UserCollectionResponse.CreateFromDiscriminatorValue);

                await PermanentlyDeleteItems(deletedItems.Value);
                Console.WriteLine($"Cleaned up {_permanentlyDeleted} deleted items so far.");
            }
        }

        private static int _existingUsers = 0;
        private void HandleExistingUsers(IEnumerable<User> users)
        {
            foreach (var user in users)
            {
                var userNumber = int.Parse(user.DisplayName.Substring("Test User ".Length));
                if (userNumber < _testUsers.Length)
                    _testUsers[userNumber] = ToEntity(user);
                _userIds.Add(user.OnPremisesImmutableId, user.Id);

                _existingUsers++;
                if (_existingUsers % 1000 == 0)
                {
                    Console.WriteLine($"Got {_existingUsers} existing users.");
                }
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

            var graphUser = await _graphServiceClient.Users.PostAsync(user);
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
