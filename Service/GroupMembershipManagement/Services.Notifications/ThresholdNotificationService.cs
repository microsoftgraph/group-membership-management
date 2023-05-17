// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Contracts.Notifications;
using Models.ThresholdNotifications;
using Repositories.Contracts;
using Models.AdaptiveCards;
using AdaptiveCards.Templating;
using DIConcreteTypes;
using Microsoft.Extensions.Options;
using Repositories.Contracts.InjectConfig;

namespace Services.Notifications
{
    public class ThresholdNotificationService : IThresholdNotificationService
    {
        private readonly IGraphGroupRepository _graphGroupRepository;
        private readonly ILocalizationRepository _localizationRepository;
        private readonly IHandleInactiveJobsConfig _handleInactiveJobsConfig;
        private readonly string _apiHostname;
        private readonly Guid _providerId;

        public ThresholdNotificationService(
            IOptions<ThresholdNotificationServiceConfig> config,
            IGraphGroupRepository graphGroupRepository,
            ILocalizationRepository localizationRepository,
            IHandleInactiveJobsConfig handleInactiveJobsConfig)
        {
            _apiHostname = config.Value.ApiHostname;
            _providerId = config.Value.ActionableEmailProviderId;
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
            _localizationRepository = localizationRepository ?? throw new ArgumentNullException(nameof(localizationRepository));
            _handleInactiveJobsConfig = handleInactiveJobsConfig ?? throw new ArgumentNullException( nameof(handleInactiveJobsConfig));
        }

        /// <inheritdoc />
        public async Task<string> CreateNotificationCardAsync(ThresholdNotification notification)
        {
            string cardJson;
            if (notification.CardState == ThresholdNotificationCardState.DefaultCard)
            {
                cardJson = _localizationRepository.TranslateSetting(CardTemplate.ThresholdNotification);
            }
            else if (notification.CardState == ThresholdNotificationCardState.DisabledCard)
            {
                cardJson = _localizationRepository.TranslateSetting(CardTemplate.ThresholdNotificationDisabled);
            }
            else
            {
                throw new NotSupportedException("Currently the Notifier trigger only supports NextCardState of DefaultCard and DisabledCard. Please check on this card");
            }

            var groupName = await _graphGroupRepository.GetGroupNameAsync(notification.TargetOfficeGroupId);
            var cardData = new ThresholdNotificationCardData
            {
                GroupName = groupName,
                ChangeQuantityForAdditions = notification.ChangeQuantityForAdditions,
                ChangeQuantityForRemovals = notification.ChangeQuantityForRemovals,
                ChangePercentageForAdditions = notification.ChangePercentageForAdditions,
                ChangePercentageForRemovals = notification.ChangePercentageForRemovals,
                ThresholdPercentageForAdditions = notification.ThresholdPercentageForAdditions,
                ThresholdPercentageForRemovals = notification.ThresholdPercentageForRemovals,
                ApiHostname = _apiHostname,
                NotificationId = $"{notification.Id}",
                ProviderId = $"{_providerId}", 
                CardCreatedTime = DateTime.UtcNow,
                JobExpirationDate = notification.CardState == ThresholdNotificationCardState.DisabledCard ?
                    notification.LastUpdatedTime.AddDays(_handleInactiveJobsConfig.NumberOfDaysBeforeDeletion) : DateTime.MinValue
            };

            var template = new AdaptiveCardTemplate(cardJson);
            var card = template.Expand(cardData);

            return card;
        }

        /// <inheritdoc />
        public string CreateNotFoundNotificationCard(Guid notificationId)
        {
            var cardJson = _localizationRepository.TranslateSetting(CardTemplate.ThresholdNotificationNotFound);
            var cardData = new ThesholdNotificationNotFoundCardData
            {
                NotificationId = $"{notificationId}",
                ProviderId = $"{_providerId}",
                CardCreatedTime = DateTime.UtcNow
            };

            var template = new AdaptiveCardTemplate(cardJson);
            var card = template.Expand(cardData);

            return card;

        }

        /// <inheritdoc />
        public async Task<string> CreateResolvedNotificationCardAsync(ThresholdNotification notification)
        {
            var cardJson = _localizationRepository.TranslateSetting(CardTemplate.ThresholdNotificationResolved);
            var resolution = _localizationRepository.TranslateSetting(notification.Resolution);
            var groupName = await _graphGroupRepository.GetGroupNameAsync(notification.TargetOfficeGroupId);
            var cardData = new ThesholdNotificationResolvedCardData
            {
                GroupName = groupName,
                ChangeQuantityForAdditions = notification.ChangeQuantityForAdditions,
                ChangeQuantityForRemovals = notification.ChangeQuantityForRemovals,
                ChangePercentageForAdditions = notification.ChangePercentageForAdditions,
                ChangePercentageForRemovals = notification.ChangePercentageForRemovals,
                ThresholdPercentageForAdditions = notification.ThresholdPercentageForAdditions,
                ThresholdPercentageForRemovals = notification.ThresholdPercentageForRemovals,
                ResolvedByUPN = notification.ResolvedByUPN,
                ResolvedTime = notification.ResolvedTime.ToString("U"),
                Resolution = resolution,
                NotificationId = $"{notification.Id}",
                ProviderId = $"{_providerId}",
                CardCreatedTime = DateTime.UtcNow
            };

            var template = new AdaptiveCardTemplate(cardJson);
            var card = template.Expand(cardData);

            return card;
        }

        /// <inheritdoc />
        public async Task<string> CreateUnauthorizedNotificationCardAsync(ThresholdNotification notification)
        {

            var groupName = await _graphGroupRepository.GetGroupNameAsync(notification.TargetOfficeGroupId);
            var cardJson = _localizationRepository.TranslateSetting(CardTemplate.ThresholdNotificationUnauthorized);
            var cardData = new ThresholdNotificationUnauthorizedCardData
            {
                GroupName = groupName,
                NotificationId = $"{notification.Id}",
                ProviderId = $"{_providerId}",
                CardCreatedTime = DateTime.UtcNow
            };

            var template = new AdaptiveCardTemplate(cardJson);
            var card = template.Expand(cardData);

            return card;
        }
    }
}