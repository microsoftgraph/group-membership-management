// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Entities;
using Microsoft.Graph;
using Microsoft.Graph.Core.Requests;
using Newtonsoft.Json.Linq;
using Repositories.Contracts;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace Repositories.GraphGroups
{
	public class GraphGroupRepository : IGraphGroupRepository
	{
		private readonly GraphServiceClient _graphServiceClient;

		public GraphGroupRepository(IAuthenticationProvider authProvider)
		{
			_graphServiceClient = new GraphServiceClient(authProvider);
		}

		public async Task<List<AzureADUser>> GetUsersInGroupTransitively(Guid objectId)
		{
			var members = await _graphServiceClient.Groups[objectId.ToString()].TransitiveMembers.Request()
							.WithMaxRetry(10)
							.Select("id")
							.GetAsync();
			var toReturn = new List<AzureADUser>(ToUsers(members.CurrentPage));
			while (members.NextPageRequest != null)
			{
				members = await members.NextPageRequest.GetAsync();
				toReturn.AddRange(ToUsers(members.CurrentPage));
			}
			return toReturn;
		}

		public async Task<IEnumerable<IAzureADObject>> GetChildrenOfGroup(Guid objectId)
		{
			var members = await _graphServiceClient.Groups[objectId.ToString()].Members.Request()
				.WithMaxRetry(10)
				.Select("id")
				.GetAsync();
			var toReturn = new List<IAzureADObject>(ToEntities(members.CurrentPage));
			while (members.NextPageRequest != null)
			{
				members = await members.NextPageRequest.GetAsync();
				toReturn.AddRange(ToEntities(members.CurrentPage));
			}
			return toReturn;
		}

		const int GraphBatchLimit = 20;
		const int ConcurrentRequests = 10;
		public Task AddUsersToGroup(IEnumerable<AzureADUser> users, AzureADGroup targetGroup)
		{
			//You can, in theory, send batches of 20 requests of 20 group adds each
			// but Graph starts saying "Service Unavailable" for a bunch of them if you do that, so only send so many at once
			// 5 seems to be the most without it starting to throw errors that have to be retried
			return BatchAndSend(users, b => MakeBulkAddRequest(b, targetGroup.ObjectId), GraphBatchLimit, 5);
		}

		public Task RemoveUsersFromGroup(IEnumerable<AzureADUser> users, AzureADGroup targetGroup)
		{
			// This, however, is the most we can send per delete batch, and it works pretty well.
			return BatchAndSend(users, b => MakeBulkRemoveRequest(b, targetGroup.ObjectId), 1, GraphBatchLimit);
		}

		delegate HttpRequestMessage MakeBulkRequest(List<AzureADUser> batch);
		private class ChunkOfUsers
		{
			public List<AzureADUser> ToSend { get; set; }
			public string Id { get; set; }

			private const int MaxBatchRetries = 5;

			// basically, whenever a batch is retried, we append the thread number after a dash
			public bool ShouldRetry => Id.Split('-')[1].Length < MaxBatchRetries;
			public ChunkOfUsers UpdateIdForRetry(int threadNumber)
			{
				Id += threadNumber;
				return this;
			}
		}

		private async Task BatchAndSend(IEnumerable<AzureADUser> users, MakeBulkRequest makeRequest, int requestMax, int batchSize)
		{
			if (!users.Any()) { return; }

			int batchRequestId = 0;
			var queuedBatches = new ConcurrentQueue<ChunkOfUsers>(
					ChunksOfSize(users, requestMax) // Chop up the users into chunks of how many per graph request (20 for add, 1 for remove)
					.Select(x => new ChunkOfUsers { ToSend = x, Id = $"{batchRequestId++}-" }));

			await Task.WhenAll(Enumerable.Range(0, ConcurrentRequests).Select(x => ProcessQueue(queuedBatches, makeRequest, x, batchSize)));
		}

		private async Task ProcessQueue(ConcurrentQueue<ChunkOfUsers> queue, MakeBulkRequest makeRequest, int threadNumber, int batchSize)
		{
			do
			{
				var toSend = new List<ChunkOfUsers>();
				while (queue.TryDequeue(out var step))
				{
					toSend.Add(step);
					if (toSend.Count == batchSize)
					{
						await ProcessBatch(queue, toSend, makeRequest, threadNumber);
						toSend.Clear();
					}
				}

				if (toSend.Any())
				{
					await ProcessBatch(queue, toSend, makeRequest, threadNumber);
				}

			} while (!queue.IsEmpty); // basically, that last ProcessBatch may have put more stuff in the queue

			//Console.WriteLine($"Thread {threadNumber} finished.");
		}

		private async Task ProcessBatch(ConcurrentQueue<ChunkOfUsers> queue, List<ChunkOfUsers> toSend, MakeBulkRequest makeRequest, int threadNumber)
		{
			//Console.WriteLine($"{threadNumber}: Sending a batch of {toSend.Count} requests.");
			int requeued = 0;
			try
			{
				await foreach (var idToRetry in await SendBatch(new BatchRequestContent(toSend.Select(x => new BatchRequestStep(x.Id, makeRequest(x.ToSend))).ToArray())))
				{
					requeued++;
					var chunkToRetry = toSend.First(x => x.Id == idToRetry);
					if (chunkToRetry.ShouldRetry)
					{
						//Console.WriteLine($"{threadNumber}: Retrying batch ID {idToRetry} later.");
						queue.Enqueue(chunkToRetry.UpdateIdForRetry(threadNumber));
					}
					else
					{
						//Console.WriteLine($"{threadNumber}: {idToRetry} has reached its retry limit and is okay to be dropped.");
					}
				}
				//Console.WriteLine($"{threadNumber}: {toSend.Count - requeued}/{toSend.Count} succeeded. {queue.Count} left.");
			}
			catch (ServiceException)
			{
				// winding up in here is a pretty rare event
				// Usually, it's because either a timeout happened or something else weird went on
				// the best thing to do is just requeue the chunks
				// but if a chunk has already been queued five times or so, drop it on the floor so we don't go forever
				// in the future, log the exception and which ones get dropped.

				foreach (var chunk in toSend)
				{
					if (chunk.ShouldRetry)
						queue.Enqueue(chunk.UpdateIdForRetry(threadNumber));
				}
			}
		}

		private async Task<IAsyncEnumerable<string>> SendBatch(BatchRequestContent tosend)
		{
			var response = await _graphServiceClient.Batch.Request().WithMaxRetry(10).PostAsync(tosend);
			return GetStepIdsToRetry(await response.GetResponsesAsync());
		}

		private static readonly HttpStatusCode[] _shouldRetry = new[] { HttpStatusCode.ServiceUnavailable, HttpStatusCode.GatewayTimeout, HttpStatusCode.BadGateway, HttpStatusCode.InternalServerError };
		private static readonly HttpStatusCode[] _isOkay = new[] { HttpStatusCode.NoContent, HttpStatusCode.NotFound, HttpStatusCode.OK };

		// These indicate that we're trying to remove a user that's already been removed.
		// Probably because an ID from earlier finally went through between the first try and the retry.
		private static readonly string[] _okayErrorMessages =
			{
				"One or more removed object references do not exist for the following modified properties: 'members'.",
				"One or more added object references already exist for the following modified properties: 'members'."
			};

		private async IAsyncEnumerable<string> GetStepIdsToRetry(Dictionary<string, HttpResponseMessage> responses)
		{
			bool beenThrottled = false;
			foreach (var kvp in responses)
			{
				//Ensure that the response messages get disposed of.
				using var response = kvp.Value;

				var status = response.StatusCode;

				string badRequestBody = string.Empty;
				// Note that the ones with empty bodies mean "this response is okay and we don't have to do anything about it."
				if (status == HttpStatusCode.BadRequest && IsOkayError(badRequestBody = await response.Content.ReadAsStringAsync())) { }
				else if (status == HttpStatusCode.NotFound && (await response.Content.ReadAsStringAsync()).Contains("does not exist or one of its queried reference-property objects are not present."))
				{
					// basically, the graph sometimes won't be able to find the group for some reason, and we have to retry then
					// but sometimes that error doesn't go away, because the thing's already been removed, so cap the number of retries.
					// I think the more accurate rule is something like "retry when adding to the group, ignore when removing from the group",
					// but since this function, by design, doesn't know which kind of thing it's doing, this is a reasonable compromise.
					// every batch is capped by number of retries, so we just have to retry here.
					yield return kvp.Key;
				}
				else if (_isOkay.Contains(status)) { }
				else if (status == HttpStatusCode.TooManyRequests)
				{
					// basically, each request in the batch will probably say it's been throttled
					// but we only need to wait the first time.
					if (!beenThrottled)
					{
						await HandleThrottling(response.Headers.RetryAfter);
						beenThrottled = true;
					}
					yield return kvp.Key;
				}
				else if (_shouldRetry.Contains(status)) { yield return kvp.Key; }
				else { /*Console.WriteLine($"Got an unexpected error: {status} {response.ReasonPhrase} {badRequestBody}.");*/ }
			}
		}

		private static bool IsOkayError(string error)
		{
			error = JObject.Parse(error)["error"]["message"].Value<string>();
			return _okayErrorMessages.Any(x => error.Contains(x));
		}
		private static Task HandleThrottling(RetryConditionHeaderValue wait)
		{
			TimeSpan waitFor = TimeSpan.FromSeconds(120);
			if (wait.Delta.HasValue) { waitFor = wait.Delta.Value; }
			if (wait.Date.HasValue) { waitFor = wait.Date.Value - DateTimeOffset.UtcNow; }
			//Console.WriteLine($"Got throttled, waiting for {waitFor.TotalSeconds} seconds.");
			return Task.Delay(waitFor);
		}

		private HttpRequestMessage MakeBulkAddRequest(List<AzureADUser> batch, Guid targetGroup)
		{
			return new HttpRequestMessage(HttpMethod.Patch, $"https://graph.microsoft.com/v1.0/groups/{targetGroup}")
			{
				Content = new StringContent(MakeAddRequestBody(batch), System.Text.Encoding.UTF8, "application/json"),
			};
		}

		private HttpRequestMessage MakeBulkRemoveRequest(List<AzureADUser> batch, Guid targetGroup)
		{
			// You have to remove users with their object ID. UPN won't work because you can only use it when the thing you're removing is
			// unambiguously a user.

			if (batch.Count != 1) { throw new ArgumentException("Batches of deletes must have exactly one item. This one has " + batch.Count); }

			var toRemove = batch.Single().ObjectId;
			return new HttpRequestMessage(HttpMethod.Delete, $"https://graph.microsoft.com/v1.0/groups/{targetGroup}/members/{toRemove}/$ref");
		}

		private static string MakeAddRequestBody(List<AzureADUser> users)
		{
			JObject body = new JObject
			{
				["members@odata.bind"] = JArray.FromObject(users.Select(x => $"https://graph.microsoft.com/v1.0/users/{x.ObjectId}"))
			};
			return body.ToString(Newtonsoft.Json.Formatting.None);
		}

		private static IEnumerable<List<T>> ChunksOfSize<T>(IEnumerable<T> enumerable, int chunkSize)
		{
			var toReturn = new List<T>();
			foreach (var item in enumerable)
			{
				if (toReturn.Count == chunkSize)
				{
					yield return toReturn;
					toReturn = new List<T>();
				}
				toReturn.Add(item);
			}
			yield return toReturn;
		}

		private IEnumerable<IAzureADObject> ToEntities(IEnumerable<DirectoryObject> fromGraph)
		{
			foreach (var directoryObj in fromGraph)
			{
				switch (directoryObj)
				{
					case User user:
						yield return new AzureADUser { ObjectId = Guid.Parse(user.Id) };
						break;
					case Group group:
						yield return new AzureADGroup { ObjectId = Guid.Parse(group.Id) };
						break;
					default:
						break;
				}
			}
		}

		private IEnumerable<AzureADUser> ToUsers(IEnumerable<DirectoryObject> fromGraph)
		{
			foreach (var directoryObj in fromGraph)
			{
				switch (directoryObj)
				{
					case User user:
						yield return new AzureADUser { ObjectId = Guid.Parse(user.Id) };
						break;
					// We only care about users
					// I'd prefer to be able to filter these out from the results on Graph's side, but the library doesn't support that yet.
					default:
						break;
				}
			}
		}
	}
}

