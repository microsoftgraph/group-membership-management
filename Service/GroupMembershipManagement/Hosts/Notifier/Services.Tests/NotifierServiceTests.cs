// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Azure;
using Azure.Core;
using Azure.Monitor.Query;
using Azure.Monitor.Query.Models;
using Entities;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Repositories.Contracts.InjectConfig;
using Services.Contracts;
using Services.Tests.Mocks;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
namespace Services.Tests
{
    [TestClass]
    public class NotifierServiceTests
    {
        private MockLoggingRepository _mockLoggingRepository = null;
        private Mock<LogsQueryClient> _logsQueryClient = new Mock<LogsQueryClient>();
        private NotifierService _notifierService = null;

        [TestInitialize]
        public void InitializeTest()
        {
            _mockLoggingRepository = new MockLoggingRepository();

            _notifierService = new NotifierService(
                _mockLoggingRepository
            );
        }

        private Response<LogsQueryResult> CreateLogsQueryResult(List<(Guid Id, double Max, double Avg)> groupRuntimes)
        {
            var columns = new List<LogsTableColumn>();
            var columnNames = new[] { "Destination", "MaxProcessingTime", "AvgProcessingTime" };
            var logsTableColumnConstructor = typeof(LogsTableColumn).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic,
                                                                                    new[] { typeof(string), typeof(LogsColumnType) });

            foreach (var name in columnNames)
            {
                var columnType = name == "Destination" ? LogsColumnType.Guid : LogsColumnType.Real;
                columns.Add(logsTableColumnConstructor.Invoke(new object[] { name, columnType }) as LogsTableColumn);
            }

            var rowsList = new List<string>();
            foreach (var group in groupRuntimes)
            {
                rowsList.Add($"[\"{group.Id}\",{group.Max},{group.Avg}]");
            }

            var tableJSON = $"{{\"name\":\"PrimaryResult\"," +
                            $"\"columns\":[{{\"name\":\"Destination\",\"type\":\"string\"}},{{\"name\":\"MaxProcessingTime\",\"type\":\"real\"}},{{\"name\":\"AvgProcessingTime\",\"type\":\"real\"}}]," +
                            $"\"rows\":[{string.Join(",", rowsList)}]}}";

            var jsonDocument = JsonDocument.Parse(tableJSON);
            var jsonElementConstructor = typeof(JsonElement).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic,
                                                                        new[] { typeof(JsonDocument), typeof(int) });

            var tableJsonElement = (JsonElement)jsonElementConstructor.Invoke(new object[] { jsonDocument, 0 });
            var logsTableConstructor = typeof(LogsTable).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic,
                                                                        new[] { typeof(string), typeof(IEnumerable<LogsTableColumn>), typeof(JsonElement) });

            JsonElement rows = default;
            foreach (var property in tableJsonElement.EnumerateObject())
            {
                if (property.NameEquals("rows"))
                {
                    rows = property.Value.Clone();
                    break;
                }
            }

            var logsTable = logsTableConstructor.Invoke(new object[] { "table", columns, rows }) as LogsTable;
            var logsQueryResultConstructor = typeof(LogsQueryResult).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, new[] { typeof(IEnumerable<LogsTable>) });
            var logsQueryResult = logsQueryResultConstructor.Invoke(new object[] { new List<LogsTable> { logsTable } }) as LogsQueryResult;

            return Response.FromValue(logsQueryResult, new TestResponse());
        }
    }
    public class TestResponse : Response
    {
        public TestResponse()
        {
        }

        public override int Status { get; }

        public override string ReasonPhrase { get; }

        public override Stream ContentStream { get; set; }
        public override string ClientRequestId { get; set; }

        public override void Dispose()
        {
            throw new NotImplementedException();
        }

        protected override bool ContainsHeader(string name)
        {
            throw new NotImplementedException();
        }

        protected override IEnumerable<HttpHeader> EnumerateHeaders()
        {
            throw new NotImplementedException();
        }

        protected override bool TryGetHeader(string name, [NotNullWhen(true)] out string value)
        {
            throw new NotImplementedException();
        }

        protected override bool TryGetHeaderValues(string name, [NotNullWhen(true)] out IEnumerable<string> values)
        {
            throw new NotImplementedException();
        }
    }
}
