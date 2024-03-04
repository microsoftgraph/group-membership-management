namespace Models.Notifications
{
    public enum NotificationMessageType
    {
        ThresholdNotification = 0,
        SyncStartedNotification = 1,
        SyncCompletedNotification = 2,
        DestinationNotExistNotification = 3,
        SourceNotExistNotification = 4,
        NotOwnerNotification = 5,
        NotValidSourceNotification= 6,
        NoDataNotification = 7,
        NormalThresholdNotification = 8,
    }
}
