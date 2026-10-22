// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Tests.Services.Infrastructure
{
    internal sealed class RecordingLogger<T> : ILogger<T>
    {
        private readonly List<LogEntry> _entries = new();
        private readonly AsyncLocal<ScopeNode> _currentScope = new();

        public IReadOnlyList<LogEntry> Entries => _entries;

        public IDisposable BeginScope<TState>(TState state)
        {
            var parent = _currentScope.Value;
            _currentScope.Value = new ScopeNode(ToValues(state), parent);
            return new ScopeRegistration(this, parent);
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter)
        {
            var scopes = new Dictionary<string, object>(StringComparer.Ordinal);
            var currentScope = _currentScope.Value;
            while (currentScope != null)
            {
                foreach (var value in currentScope.Values)
                {
                    scopes.TryAdd(value.Key, value.Value);
                }

                currentScope = currentScope.Parent;
            }

            _entries.Add(new LogEntry(
                eventId,
                logLevel,
                formatter(state, exception),
                ToValues(state),
                scopes));
        }

        private static IReadOnlyDictionary<string, object> ToValues<TState>(
            TState state)
        {
            if (state is not IEnumerable<KeyValuePair<string, object>> values)
            {
                return new Dictionary<string, object>();
            }

            return values
                .GroupBy(value => value.Key, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.Last().Value,
                    StringComparer.Ordinal);
        }

        internal sealed record LogEntry(
            EventId EventId,
            LogLevel Level,
            string Message,
            IReadOnlyDictionary<string, object> State,
            IReadOnlyDictionary<string, object> Scopes);

        private sealed class ScopeNode
        {
            public ScopeNode(
                IReadOnlyDictionary<string, object> values,
                ScopeNode parent)
            {
                Values = values;
                Parent = parent;
            }

            public IReadOnlyDictionary<string, object> Values { get; }
            public ScopeNode Parent { get; }
        }

        private sealed class ScopeRegistration : IDisposable
        {
            private readonly RecordingLogger<T> _owner;
            private readonly ScopeNode _parent;
            private bool _disposed;

            public ScopeRegistration(
                RecordingLogger<T> owner,
                ScopeNode parent)
            {
                _owner = owner;
                _parent = parent;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _owner._currentScope.Value = _parent;
                _disposed = true;
            }
        }
    }
}
