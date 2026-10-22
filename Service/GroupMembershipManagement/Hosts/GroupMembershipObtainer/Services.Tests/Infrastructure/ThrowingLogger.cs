// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Tests.Services.Infrastructure
{
    internal sealed class ThrowingLogger<T> : ILogger<T>
    {
        private readonly HashSet<int> _eventIds;
        private readonly List<int> _attemptedEventIds = new();

        public ThrowingLogger(params int[] eventIds)
        {
            _eventIds = eventIds.ToHashSet();
        }

        public IReadOnlyList<int> AttemptedEventIds => _attemptedEventIds;

        public IDisposable BeginScope<TState>(TState state) =>
            NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter)
        {
            _attemptedEventIds.Add(eventId.Id);
            if (_eventIds.Contains(eventId.Id))
            {
                throw new InvalidOperationException(
                    $"Injected logging failure for event {eventId.Id}.");
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new();

            public void Dispose()
            {
            }
        }
    }
}
