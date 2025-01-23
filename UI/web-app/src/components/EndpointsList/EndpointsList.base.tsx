// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import * as React from 'react';
import { ActionButton, mergeStyles, MessageBar, MessageBarButton, MessageBarType, Stack, Text } from '@fluentui/react';
import { IProcessedStyleSet, classNamesFunction } from '@fluentui/react';
import { IEndpointsListProps, IEndpointsListStyleProps, IEndpointsListStyles } from './EndpointsList.types';
import { useStrings } from '../../store/hooks';
import { useSelector } from 'react-redux';
import { selectOutlookWarningUrl } from '../../store/settings.slice';
import vivaEngageLogo from '../../assets/vivaengagelogo.png';

const getClassNames = classNamesFunction<IEndpointsListStyleProps, IEndpointsListStyles>();

export const EndpointsListBase: React.FunctionComponent<IEndpointsListProps> = (props) => {
  const { className, styles, endpoints, groupName, showOutlookWarning } = props;
  const classNames: IProcessedStyleSet<IEndpointsListStyles> = getClassNames(styles, {
    className,
  });
  const strings = useStrings();

  const hasRequiredEndpoints = () => {
    if (!endpoints) return false;
    return ['Outlook', 'Yammer', 'SharePoint', 'Microsoft Teams', 'SecurityGroup'].some((endpoint) =>
      endpoints.includes(endpoint)
    );
  };

  const outlookWarningUrl = useSelector(selectOutlookWarningUrl);
  const SharePointDomain = `${process.env.REACT_APP_SHAREPOINTDOMAIN}`;
  const domainName = `${process.env.REACT_APP_DOMAINNAME}`;
  const groupNameClean: string | undefined = groupName?.replace(/\s/g, '');

  const openOutlookLink = (): void => {
    const url = `https://outlook.office.com/mail/group/${domainName}/${groupNameClean}`;
    window.open(url, '_blank', 'noopener,noreferrer');
  };

  const onClickOutlookWarning = (): void => {
    window.open(outlookWarningUrl, '_blank', 'noopener,noreferrer');
  };

  const openSharePointLink = (): void => {
    const url = `https://${SharePointDomain}/sites/${groupNameClean}`;
    window.open(url, '_blank', 'noopener,noreferrer');
  };

  const openTeamsLink = (): void => {
    const url = `https://teams.microsoft.com/l/team/${domainName}/${groupNameClean}`;
    window.open(url, '_blank', 'noopener,noreferrer');
  };

  const vivaEngageLabelClass = mergeStyles({
    marginRight: '0px',
    marginLeft: '6px',
    whiteSpace: 'nowrap',
  });

  if (!hasRequiredEndpoints()) return null;

  return endpoints && endpoints.length === 0 ? (
    <></>
  ) : endpoints && endpoints.every((endpoint) => endpoint === 'SecurityGroup') ? (
    <Text className={classNames.itemTitle} block>
      {strings.ManageMembership.labels.entraSecurityGroup}
    </Text>
  ) : (
    <Stack.Item align="start">
      <Text className={classNames.itemTitle} block>
        {strings.ManageMembership.labels.appsUsed}
      </Text>
      <Text className={classNames.itemData} block>
        <div className={classNames.endpointsContainer}>
          {endpoints?.includes('Outlook') && (
            <div className={classNames.outlookContainer}>
              <ActionButton iconProps={{ iconName: 'OutlookLogo' }} onClick={() => openOutlookLink()}>
                Outlook
              </ActionButton>
              {outlookWarningUrl && showOutlookWarning && (
                <MessageBar
                  messageBarType={MessageBarType.warning}
                  className={classNames.outlookWarning}
                  isMultiline={false}
                  actions={
                    <MessageBarButton onClick={() => onClickOutlookWarning()}>{strings.learnMore}</MessageBarButton>
                  }
                >
                  {strings.ManageMembership.labels.outlookWarning}
                </MessageBar>
              )}
            </div>
          )}
          {endpoints?.includes('SharePoint') && (
            <ActionButton iconProps={{ iconName: 'SharePointLogo' }} onClick={() => openSharePointLink()}>
              SharePoint
            </ActionButton>
          )}
          {endpoints?.includes('Microsoft Teams') && (
            <ActionButton iconProps={{ iconName: 'TeamsLogo' }} onClick={() => openTeamsLink()}>
              Teams
            </ActionButton>
          )}
          {endpoints?.includes('Yammer') && (
            <div className={classNames.yammerContainer}>
              <Stack horizontal verticalAlign="center">
                <div
                  className={mergeStyles({
                    backgroundImage: `url(${vivaEngageLogo})`,
                    backgroundSize: 'contain',
                    backgroundRepeat: 'no-repeat',
                    width: '24px',
                    height: '24px',
                  })}
                  aria-hidden="true"
                ></div>
                <Text className={vivaEngageLabelClass}>Viva Engage</Text>
              </Stack>
            </div>
          )}
        </div>
      </Text>
    </Stack.Item>
  );
};
