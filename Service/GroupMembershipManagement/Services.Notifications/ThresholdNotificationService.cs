// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

using Services.Contracts.Notifications;
using Models.ThresholdNotifications;
using Repositories.Contracts;
using Models.AdaptiveCards;
using AdaptiveCards.Templating;
using DIConcreteTypes;
using Microsoft.Extensions.Options;

namespace Services.Notifications
{
    public class ThresholdNotificationService : IThresholdNotificationService
    {
        private readonly IGraphGroupRepository _graphGroupRepository;
        private readonly ILocalizationRepository _localizationRepository;
        private readonly string _apiHostname;
        private readonly Guid _providerId;

        public ThresholdNotificationService(
            IOptions<ThresholdNotificationServiceConfig> config,
            IGraphGroupRepository graphGroupRepository,
            ILocalizationRepository localizationRepository)
        {
            _graphGroupRepository = graphGroupRepository ?? throw new ArgumentNullException(nameof(graphGroupRepository));
            _localizationRepository = localizationRepository ?? throw new ArgumentNullException(nameof(localizationRepository));
            _apiHostname = config.Value.ApiHostname;
            _providerId = config.Value.ActionableEmailProviderId;
        }

        /// <inheritdoc />
        public async Task<string> CreateNotificationCardAsync(ThresholdNotification notification)
        {
            var cardJson = _localizationRepository.TranslateSetting(CardTemplate.ThresholdNotification);
            var groupName = await _graphGroupRepository.GetGroupNameAsync(notification.TargetOfficeGroupId);
            var cardData = new ThesholdNotificationCardData
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
                CardCreatedTime = DateTime.UtcNow
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
                ProviderId = $"{_providerId}"
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
                ProviderId = $"{_providerId}"
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
            var cardData = new ThesholdNotificationUnauthorizedCardData
            {
                GroupName = groupName,
                NotificationId = $"{notification.Id}",
                ProviderId = $"{_providerId}"
            };

            var template = new AdaptiveCardTemplate(cardJson);
            var card = template.Expand(cardData);

            return card;
        }
    }
}