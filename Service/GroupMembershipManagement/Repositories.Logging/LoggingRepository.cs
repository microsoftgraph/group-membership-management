// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Entities;
using Newtonsoft.Json;
using Repositories.Contracts;
using Repositories.Contracts.InjectConfig;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
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
        public LoggingRepository(ILogAnalyticsSecret<LoggingRepository> logAnalytics)
        {            
            if (logAnalytics == null) throw new ArgumentNullException(nameof(logAnalytics));
            _workSpaceId = logAnalytics.WorkSpaceId ?? throw new ArgumentNullException(nameof(logAnalytics.WorkSpaceId));
            _sharedKey = logAnalytics.SharedKey ?? throw new ArgumentNullException(nameof(logAnalytics.SharedKey));
            _location = logAnalytics.Location ?? throw new ArgumentNullException(nameof(logAnalytics.Location));
        }

        public async Task LogMessageAsync(LogMessage logMessage)
        {
            var serializedMessage = JsonConvert.SerializeObject(
            new
            {
                instanceId = logMessage.InstanceId,
                runId = logMessage.RunId,
                messageTypeName = logMessage.MessageTypeName,
                message = logMessage.Message,
                location = _location
            });

            var url = $"https://{_workSpaceId}.ods.opinsights.azure.com/api/logs?api-version=2016-04-01";
            var _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("Log-Type", "ApplicationLog");
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
            var result = await response.Content.ReadAsStringAsync();
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
    }
}
