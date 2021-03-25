// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Entities;
using Newtonsoft.Json;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Repositories.Logging
{
    public class LoggingRepository : ILoggingRepository
    {
        private readonly string _workSpaceId;
        private readonly string _sharedKey;
        private readonly string _location;

        // you should only have one httpClient for the life of your program
        // see https://aspnetmonsters.com/2016/08/2016-08-27-httpclientwrong/?fbclid=IwAR2aNRweTjGdx5Foev4XvHj2Xldeg_UAb6xW3eLTFQDB7Xghv65LvrVa5wA
        private static readonly HttpClient _httpClient = MakeClient();

        public Dictionary<string, string> SyncJobProperties { get; set; }

        public LoggingRepository(ILogAnalyticsSecret<LoggingRepository> logAnalytics)
        {
            if (logAnalytics == null) throw new ArgumentNullException(nameof(logAnalytics));
            _workSpaceId = logAnalytics.WorkSpaceId ?? throw new ArgumentNullException(nameof(logAnalytics.WorkSpaceId));
            _sharedKey = logAnalytics.SharedKey ?? throw new ArgumentNullException(nameof(logAnalytics.SharedKey));
            _location = logAnalytics.Location ?? throw new ArgumentNullException(nameof(logAnalytics.Location));
        }

        private static HttpClient MakeClient()
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Log-Type", "ApplicationLog");
            return client;
        }

        public async Task LogMessageAsync(LogMessage logMessage, [CallerMemberName] string caller = "", [CallerFilePath] string file = "")
        {
            var properties = CreatePropertiesDictionary(logMessage);
            properties.Add("location", _location);

            if (!string.IsNullOrWhiteSpace(caller))
                properties.Add("event", caller);

            if (!string.IsNullOrWhiteSpace(file))
                properties.Add("operation", Path.GetFileNameWithoutExtension(file));

            var serializedMessage = JsonConvert.SerializeObject(properties);

            var url = $"https://{_workSpaceId}.ods.opinsights.azure.com/api/logs?api-version=2016-04-01";
            var dateString = DateTime.UtcNow.ToString("r");
            var jsonBytes = Encoding.UTF8.GetBytes(serializedMessage);
            var contentType = "application/json";
            var message = $"POST\n{jsonBytes.Length}\n{contentType}\nx-ms-date:{dateString}\n/api/logs";
            var signature = BuildSignature(message, _sharedKey);
            var scheme = "SharedKey";
            var parameter = $"{_workSpaceId}:{signature}";
            var httpRequestMessage = new HttpRequestMessage(HttpMethod.Post, url);
            httpRequestMessage.Headers.Authorization = new AuthenticationHeaderValue(scheme, parameter);
            httpRequestMessage.Headers.Add("x-ms-date", dateString);
            httpRequestMessage.Content = new StringContent(serializedMessage, Encoding.UTF8);
            httpRequestMessage.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            var response = await _httpClient.SendAsync(httpRequestMessage);
            response.EnsureSuccessStatusCode();
        }

        private string BuildSignature(string message, string secret)
        {
            var encoding = new ASCIIEncoding();
            var keyByte = Convert.FromBase64String(secret);
            var messageBytes = encoding.GetBytes(message);
            using (var hmacsha256 = new HMACSHA256(keyByte))
            {
                var hash = hmacsha256.ComputeHash(messageBytes);
                return Convert.ToBase64String(hash);
            }
        }

        private Dictionary<string, string> CreatePropertiesDictionary(LogMessage logMessage)
        {
            var logMessageProperties = logMessage.ToDictionary();
            if (SyncJobProperties?.Keys.Any() ?? false)
            {
                foreach (var key in SyncJobProperties.Keys)
                {
                    if (!logMessageProperties.ContainsKey(key))
                    {
                        logMessageProperties.Add(key, SyncJobProperties[key]);
                    }
                    else if (string.IsNullOrWhiteSpace(logMessageProperties[key]) && !string.IsNullOrWhiteSpace(SyncJobProperties[key]))
                    {
                        logMessageProperties[key] = SyncJobProperties[key];
                    }
                }
            }

            return logMessageProperties;
        }
    }
}
