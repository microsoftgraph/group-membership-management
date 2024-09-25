
# GMM Notification Documentation

## Introduction

This document provides a detailed overview of all notifications sent by GMM. It includes information on each notification type, the purpose behind each message, the format used (adaptive card or basic email), and a visual example. This will serve as a comprehensive guide for understanding the structure and role of notifications within the GMM product.

## Key Areas of Focus

- **Purpose of Notification**: Why the notification is being sent and to whom.
- **Email Format**: Whether the notification uses adaptive card format or standard email format.
- **Triggered By**: The function or service that triggers this notification.
- **Example**: A visual representation of the notification 

---

## 1. Notification Name: `SyncStartedNotification`

### Purpose:
This email notifies the user that a synchronization job has started. It ensures the user is aware of the process initiation.

### Email Format:
- Adaptive Card: Yes
- Visual Example: 

![SyncStartedNotification](/Documentation/NotificationImages/SyncStartedNotification.png)

### Triggered By:
- **JobTrigger Function**: Responsible for initiating this notification when a sync job begins.

---

## 2. Notification Name: `SyncCompletedNotification`

### Purpose:
Sent to inform the user when a synchronization job is successfully completed. It helps the user know when their job has finished processing.

### Email Format:
- Adaptive Card: Yes
- Visual Example: 

![SyncCompletedNotification](/Documentation/NotificationImages/SyncCompletedNotification.png)

### Triggered By:
- **GraphUpdater** or **TeamsChannelUpdater**: Responsible for triggering the email when the sync job is marked as completed.

---

## 3. Notification Name: `DestinationNotExistNotification`

### Purpose:
This email informs the user that synchronization was disabled because the destination group does not exist. It helps in troubleshooting group-based issues during sync.

### Email Format:
- Adaptive Card: Yes
- Visual Example: 

![DestinationNotExistNotification](/Documentation/NotificationImages/DestinationNotExistNotification.png)

### Triggered By:
- **JobTrigger** and **GraphUpdater** Functions: These functions initiate this email if the destination group does not exist during the sync process.

---

## 4. Notification Name: `NoDataNotification`

### Purpose:
Informs the user that no data was found for the requested sync, providing clarity on the results of the sync operation.

### Email Format:
- Adaptive Card: Yes
- Visual Example: 

![NoDataNotification](/Documentation/NotificationImages/NoDataNotification.png)

### Triggered By:
- **MembershipAggregator Function**: Responsible for sending this notification when no data is available during aggregation.

---

## 5. Notification Name: `NotOwnerNotification`

### Purpose:
Alerts the user that a synchronization job has been paused due to GMM is not an owner of the group, helping keep users informed about the status of their jobs.

### Email Format:
- Adaptive Card: Yes
- Visual Example: 

![NotOwnerNotification](/Documentation/NotificationImages/NotOwnerNotification.png)

### Triggered By:
- **JobTrigger Function**: This function triggers the email after checking whether GMM is an owner of the group.

---

## 6. Notification Name: `NotValidSourceNotification`

### Purpose:
This email informs the user that synchronization was disabled because the source group is a not valid guid. It helps in troubleshooting group-based issues during sync.

### Email Format:
- Adaptive Card: Yes
- Visual Example: 

![NotValidSourceNotification](/Documentation/NotificationImages/NotValidSourceNotification.png)

### Triggered By:
- **GroupMembershipObtainer Function**: This function triggers the email after checking whether source group is a valid guid.

---

## 7. Notification Name: `SourceNotExistNotification`

### Purpose:
This email informs the user that synchronization was disabled because the source group no longer exist. It helps in troubleshooting group-based issues during sync.

### Email Format:
- Adaptive Card: Yes
- Visual Example:

![SourceNotExistNotification](/Documentation/NotificationImages/SourceNotExistNotification.png)

### Triggered By:
- **GroupMembershipObtainer Function**: These functions initiate this email if the source group does not exist during the sync process.

---

## 8. Notification Name: `InactiveSyncJobNotification`

### Purpose:
This email informs the user that their group’s synchronization with GMM has been disabled due to inactivity. It helps the user understand that their group no longer syncs with GMM and provides reasons for the inactivity, including the possibility that the source or destination group no longer exists, or that syncs were paused for too long.

### Email Format:
- Adaptive Card: Yes
- Visual Example:

![InactiveSyncJobNotification](/Documentation/NotificationImages/InactiveSyncJobNotification.png)

### Triggered By:
- **AzureMaintenance Function**: This function triggers the email if the group has been inactive for reasons such as the group no longer existing or the sync being paused for more than 30 days.

---

## 9. Notification Name: `GuestUserFailureNotification`

### Purpose:
This email informs the user that GMM was unable to add guest users to the destination group due to restrictions in the destination group’s settings, but it still processed the remaining user changes. It helps the user identify configuration issues related to guest user addition and informs them of actions that were still successfully performed by GMM.

### Email Format:
- Adaptive Card: Yes
- Visual Example:

![GuestUserFailureNotification](/Documentation/NotificationImages/GuestUserFailureNotification.png)

### Triggered By:
- **GraphUpdater Function**: This function triggers the email when guest users cannot be added to a destination group due to configuration restrictions in the destination settings.

---

## 10. Notification Name: `GuestUserFailureNotification`

### Purpose:
This email informs the user that GMM was unable to add guest users to the destination group due to restrictions in the destination group’s settings, but it still processed the remaining user changes. It helps the user identify configuration issues related to guest user addition and informs them of actions that were still successfully performed by GMM.

### Email Format:
- Adaptive Card: Yes
- Visual Example:

![GuestUserFailureNotification](/Documentation/NotificationImages/GuestUserFailureNotification.png)

### Triggered By:
- **GraphUpdater Function**: This function triggers the email when guest users cannot be added to a destination group due to configuration restrictions in the destination settings.

---


## Conclusion

This document outlines the notifications sent by GMM, detailing the purpose, handling logic, and customization options for each. By centralizing email notifications under the Notifier function and using a consistent approach with adaptive cards, we aim to streamline communication and enhance the user experience.
