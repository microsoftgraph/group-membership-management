using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.ServiceBus.Core;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Tests.FunctionApps.Mocks
{
	class MockMessageSession : IMessageSession
	{
		public List<string> CompletedLockTokens { get; private set; } = new List<string>();
		public bool Closed { get; private set; } = false;

		public string SessionId { get; set; }
		public DateTime LockedUntilUtc => throw new NotImplementedException();

		public long LastPeekedSequenceNumber => throw new NotImplementedException();

		public int PrefetchCount { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

		public ReceiveMode ReceiveMode => throw new NotImplementedException();

		public string ClientId => throw new NotImplementedException();

		public bool IsClosedOrClosing => throw new NotImplementedException();

		public string Path => throw new NotImplementedException();

		public TimeSpan OperationTimeout { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

		public ServiceBusConnection ServiceBusConnection => throw new NotImplementedException();

		public bool OwnsConnection => throw new NotImplementedException();

		public IList<ServiceBusPlugin> RegisteredPlugins => throw new NotImplementedException();

		public Task AbandonAsync(string lockToken, IDictionary<string, object> propertiesToModify = null)
		{
			throw new NotImplementedException();
		}

		public Task CloseAsync()
		{
			if (Closed)
				throw new InvalidOperationException("Can't close the same session twice!");
			Closed = true;
			return Task.CompletedTask;
		}

		public Task CompleteAsync(IEnumerable<string> lockTokens)
		{
			CompletedLockTokens.AddRange(lockTokens);
			return Task.CompletedTask;
		}

		public Task CompleteAsync(string lockToken)
		{
			throw new NotImplementedException();
		}

		public Task DeadLetterAsync(string lockToken, IDictionary<string, object> propertiesToModify = null)
		{
			throw new NotImplementedException();
		}

		public Task DeadLetterAsync(string lockToken, string deadLetterReason, string deadLetterErrorDescription = null)
		{
			throw new NotImplementedException();
		}

		public Task DeferAsync(string lockToken, IDictionary<string, object> propertiesToModify = null)
		{
			throw new NotImplementedException();
		}

		public Task<byte[]> GetStateAsync()
		{
			throw new NotImplementedException();
		}

		public Task<Message> PeekAsync()
		{
			throw new NotImplementedException();
		}

		public Task<IList<Message>> PeekAsync(int maxMessageCount)
		{
			throw new NotImplementedException();
		}

		public Task<Message> PeekBySequenceNumberAsync(long fromSequenceNumber)
		{
			throw new NotImplementedException();
		}

		public Task<IList<Message>> PeekBySequenceNumberAsync(long fromSequenceNumber, int messageCount)
		{
			throw new NotImplementedException();
		}

		public Task<Message> ReceiveAsync()
		{
			throw new NotImplementedException();
		}

		public Task<Message> ReceiveAsync(TimeSpan operationTimeout)
		{
			throw new NotImplementedException();
		}

		public Task<IList<Message>> ReceiveAsync(int maxMessageCount)
		{
			throw new NotImplementedException();
		}

		public Task<IList<Message>> ReceiveAsync(int maxMessageCount, TimeSpan operationTimeout)
		{
			throw new NotImplementedException();
		}

		public Task<Message> ReceiveDeferredMessageAsync(long sequenceNumber)
		{
			throw new NotImplementedException();
		}

		public Task<IList<Message>> ReceiveDeferredMessageAsync(IEnumerable<long> sequenceNumbers)
		{
			throw new NotImplementedException();
		}

		public void RegisterMessageHandler(Func<Message, CancellationToken, Task> handler, Func<ExceptionReceivedEventArgs, Task> exceptionReceivedHandler)
		{
			throw new NotImplementedException();
		}

		public void RegisterMessageHandler(Func<Message, CancellationToken, Task> handler, MessageHandlerOptions messageHandlerOptions)
		{
			throw new NotImplementedException();
		}

		public void RegisterPlugin(ServiceBusPlugin serviceBusPlugin)
		{
			throw new NotImplementedException();
		}

		public Task RenewLockAsync(Message message)
		{
			throw new NotImplementedException();
		}

		public Task<DateTime> RenewLockAsync(string lockToken)
		{
			throw new NotImplementedException();
		}

		public Task RenewSessionLockAsync()
		{
			throw new NotImplementedException();
		}

		public Task SetStateAsync(byte[] sessionState)
		{
			throw new NotImplementedException();
		}

		public void UnregisterPlugin(string serviceBusPluginName)
		{
			throw new NotImplementedException();
		}
	}
}
