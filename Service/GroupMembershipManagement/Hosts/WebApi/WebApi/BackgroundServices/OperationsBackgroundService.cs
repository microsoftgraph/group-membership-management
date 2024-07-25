// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using Models;
using Repositories.Contracts;
using Services.Contracts;
using Services.WebApi.Contracts;

namespace WebApi.BackgroundServices
{
    public class OperationsBackgroundService : BackgroundService
    {
        private readonly IResourceManagerService _resourceManagerService;
        private readonly IOperationsTaskQueue _backgroundTaskQueue;
        private readonly ILoggingRepository _loggingRepository;
        private readonly IServiceProvider _services;

        public OperationsBackgroundService(IResourceManagerService resourceManagerService,
                                           IOperationsTaskQueue backgroundTaskQueue,
                                           ILoggingRepository loggingRepository,
                                           IServiceProvider services)
        {
            _resourceManagerService = resourceManagerService ?? throw new ArgumentNullException(nameof(resourceManagerService));
            _backgroundTaskQueue = backgroundTaskQueue ?? throw new ArgumentNullException(nameof(backgroundTaskQueue));
            _loggingRepository = loggingRepository ?? throw new ArgumentNullException(nameof(loggingRepository));
            _services = services ?? throw new ArgumentNullException(nameof(services));
        }

        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var operationDetails = await _backgroundTaskQueue.DequeueAsync();

                    if (operationDetails != null)
                    {
                        if (operationDetails.Operation == Operations.Reset)
                        {
                            await _resourceManagerService.ResetWebSitesAsync(operationDetails.RequestorId, cancellationToken);
                            await SetStatusAsync(ServiceStatuses.Running, operationDetails.RequestorId);
                            await _loggingRepository
                                    .LogMessageAsync(new LogMessage
                                    {
                                        Message = "Reset operation completed."
                                    });
                        }
                        else if (operationDetails.Operation == Operations.Stop)
                        {
                            await _resourceManagerService.StopWebSitesAsync(operationDetails.RequestorId, cancellationToken);
                            await SetStatusAsync(ServiceStatuses.Stopped, operationDetails.RequestorId);
                            await _loggingRepository
                                    .LogMessageAsync(new LogMessage
                                    {
                                        Message = "Stop operation completed."
                                    });
                        }
                        else if (operationDetails.Operation == Operations.Start)
                        {
                            await _resourceManagerService.StartWebSitesAsync(operationDetails.RequestorId, cancellationToken);
                            await SetStatusAsync(ServiceStatuses.Running, operationDetails.RequestorId);
                            await _loggingRepository
                                    .LogMessageAsync(new LogMessage
                                    {
                                        Message = "Start operation completed."
                                    });
                        }
                    }

                    await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // No op, handle cancellation
                }
                catch (Exception ex)
                {
                    await _loggingRepository
                            .LogMessageAsync(new LogMessage
                            {
                                Message = $"Unexpected error in {nameof(OperationsBackgroundService)}\n{ex.Message}"
                            });

                    await Task.Delay(TimeSpan.FromSeconds(60), cancellationToken);
                }
            }
        }

        private async Task SetStatusAsync(ServiceStatuses status, Guid requestorId)
        {
            using (var scope = _services.CreateScope())
            {
                var scopedStatusRepository = scope.ServiceProvider.GetRequiredService<IServiceStatusRepository>();
                await scopedStatusRepository.SetServiceStatusAsync(status, requestorId);
            }
        }
    }
}
