// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React from 'react';
import { useSelector } from 'react-redux';
import {
  classNamesFunction,
  IProcessedStyleSet,
  Link,
  MessageBar,
  MessageBarType,
} from '@fluentui/react';
import { IAlertBannerProps, IAlertBannerStyleProps, IAlertBannerStyles } from './AlertBanner.types';
import { selectAlertBannerConfig } from '../../store/alertBanner.slice';

const getClassNames = classNamesFunction<IAlertBannerStyleProps, IAlertBannerStyles>();

/**
 * Returns true when the supplied config should be shown to the user right now:
 * the banner is enabled and the current time falls within [startDate, endDate).
 */
export const isAlertBannerVisible = (
  isEnabled: boolean,
  startDate: string,
  endDate: string,
  now: Date = new Date()
): boolean => {
  if (!isEnabled) {
    return false;
  }
  const start = new Date(startDate);
  const end = new Date(endDate);
  if (isNaN(start.getTime()) || isNaN(end.getTime())) {
    return false;
  }
  return start <= now && end > now;
};

/**
 * Only https:// links are rendered as clickable links (defense-in-depth against
 * javascript: and other unsafe protocols; the backend also enforces this).
 */
const isSafeHttpsLink = (url: string | null | undefined): url is string =>
  typeof url === 'string' && /^https:\/\//i.test(url.trim());

export const AlertBannerBase: React.FunctionComponent<IAlertBannerProps> = (props: IAlertBannerProps) => {
  const { className, styles } = props;
  const config = useSelector(selectAlertBannerConfig);

  const classNames: IProcessedStyleSet<IAlertBannerStyles> = getClassNames(styles, { className });

  if (!config || !isAlertBannerVisible(config.isEnabled, config.startDate, config.endDate)) {
    return null;
  }

  const hasSafeLink = isSafeHttpsLink(config.linkUrl);
  const linkDisplayText = config.linkText && config.linkText.trim().length > 0 ? config.linkText : config.linkUrl;

  return (
    <div className={classNames.root} data-testid="alert-banner">
      <MessageBar
        className={classNames.messageBar}
        messageBarType={MessageBarType.warning}
        isMultiline={true}
        dismissButtonAriaLabel="Close"
        role="alert"
      >
        {/* Rendered as plain text children; React escapes HTML entities so no XSS. */}
        {config.message}
        {hasSafeLink && (
          <Link
            className={classNames.link}
            href={config.linkUrl as string}
            target="_blank"
            rel="noopener noreferrer"
          >
            {linkDisplayText}
          </Link>
        )}
      </MessageBar>
    </div>
  );
};
