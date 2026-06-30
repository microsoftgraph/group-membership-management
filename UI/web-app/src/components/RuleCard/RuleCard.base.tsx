// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import React, { useEffect, useRef } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import {
  classNamesFunction,
  IProcessedStyleSet,
  Persona,
  PersonaSize,
  Text,
} from '@fluentui/react';
import { useTheme } from '@fluentui/react/lib/Theme';
import { RuleCardStyleProps, RuleCardStyles, RuleCardProps } from './RuleCard.types';
import { AppDispatch } from '../../store';
import { useStrings } from '../../store/hooks';
import { SourcePartType } from '../../models/SourcePartType';
import { HRSourcePartSource } from '../../models/HRSourcePart';
import { IsGroupMembershipSourcePartQuery } from '../../models/GroupMembershipSourcePart';
import { selectSource, selectIsSourceLoading } from '../../store/sqlMembershipSources.slice';
import { fetchDefaultSqlMembershipSource } from '../../store/sqlMembershipSources.api';
import { selectObjectIdEmployeeIdMapping } from '../../store/orgLeaderDetails.slice';
import { fetchOrgLeaderDetailsUsingId } from '../../store/orgLeaderDetails.api';
import { useSelectedGroupById } from '../../store/groupPart.slice';
import { searchDestinations } from '../../store/manageMembership.api';

const getClassNames = classNamesFunction<RuleCardStyleProps, RuleCardStyles>();

type DetailRow = {
  label: string;
  value: string;
  showPersona: boolean;
};

export const RuleCardBase: React.FunctionComponent<RuleCardProps> = (props: RuleCardProps) => {
  const { className, styles, part, selected, onSelect } = props;
  const classNames: IProcessedStyleSet<RuleCardStyles> = getClassNames(styles, {
    className,
    theme: useTheme(),
    selected,
  });
  const strings = useStrings();
  const ruleStrings = strings.Components.RuleCard;
  const dispatch = useDispatch<AppDispatch>();

  const hrSource = useSelector(selectSource);
  const isSourceLoading = useSelector(selectIsSourceLoading);
  const orgLeaderMapping = useSelector(selectObjectIdEmployeeIdMapping);

  const groupId = IsGroupMembershipSourcePartQuery(part.query) ? part.query.source : '';
  const groupPersona = useSelectedGroupById(groupId);

  const isExclusionary = part.query.exclusionary ?? false;
  const managerId = part.query.type === SourcePartType.HR
    ? (part.query.source as HRSourcePartSource)?.manager?.id
    : undefined;

  // Resolve the org leader display name when an HR rule references a manager.
  useEffect(() => {
    if (managerId && orgLeaderMapping[managerId] === undefined) {
      dispatch(fetchOrgLeaderDetailsUsingId({ employeeId: managerId, partId: part.id }));
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [managerId]);

  // Resolve the group name/alias when a Group rule references a group id.
  useEffect(() => {
    if (part.query.type === SourcePartType.GroupMembership && groupId && !groupPersona) {
      dispatch(searchDestinations(groupId));
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [groupId]);

  // Resolve the custom source label ("friendly name") for HR rules. Fetch the default
  // source once if its custom label hasn't been loaded yet (e.g. a stale/empty store),
  // so the card reflects the latest configured label.
  const sourceFetchedRef = useRef(false);
  useEffect(() => {
    if (
      part.query.type === SourcePartType.HR &&
      !isSourceLoading &&
      !sourceFetchedRef.current &&
      !hrSource?.customLabel
    ) {
      sourceFetchedRef.current = true;
      dispatch(fetchDefaultSqlMembershipSource());
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [part.query.type, hrSource, isSourceLoading]);

  const getTypeLabel = (): string => {
    switch (part.query.type) {
      case SourcePartType.HR:
        return hrSource?.customLabel || hrSource?.name || ruleStrings.typeHRFallback;
      case SourcePartType.GroupMembership:
        return ruleStrings.typeGroup;
      case SourcePartType.PlaceMembership:
        return ruleStrings.typePlace;
      case SourcePartType.GroupOwnership:
        return ruleStrings.typeGroupOwnership;
      default:
        return '';
    }
  };

  const formatDepth = (depth?: number): string => {
    if (!managerId) return ruleStrings.notSet;
    if (depth === undefined || depth <= 1) return ruleStrings.allLevels;
    const levels = depth - 1;
    const template = levels === 1 ? ruleStrings.levelDown : ruleStrings.levelsDown;
    return template.replace('{0}', levels.toString());
  };

  const getDetailRows = (): DetailRow[] => {
    switch (part.query.type) {
      case SourcePartType.HR: {
        const source = part.query.source as HRSourcePartSource;
        const orgLeaderName = managerId ? orgLeaderMapping[managerId]?.text : undefined;
        return [
          {
            label: ruleStrings.orgLeaderLabel,
            value: orgLeaderName || ruleStrings.notSet,
            showPersona: !!orgLeaderName,
          },
          {
            label: ruleStrings.depthLabel,
            value: formatDepth(source?.manager?.depth),
            showPersona: false,
          },
        ];
      }
      case SourcePartType.GroupMembership:
        return [
          {
            label: ruleStrings.nameLabel,
            value: groupPersona?.text || ruleStrings.notSet,
            showPersona: !!groupPersona?.text,
          },
          {
            label: ruleStrings.aliasLabel,
            value: groupPersona?.secondaryText || ruleStrings.notSet,
            showPersona: !!groupPersona?.secondaryText,
          },
        ];
      default:
        return [];
    }
  };

  const detailRows = getDetailRows();

  const handleSelect = () => onSelect(part.id);

  const handleKeyDown = (event: React.KeyboardEvent<HTMLDivElement>) => {
    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      handleSelect();
    }
  };

  return (
    <div
      className={classNames.root}
      role="button"
      tabIndex={0}
      aria-pressed={selected}
      aria-label={ruleStrings.selectRuleAria.replace('{0}', part.title)}
      onClick={handleSelect}
      onKeyDown={handleKeyDown}
    >
      <div className={classNames.badges}>
        <span className={isExclusionary ? classNames.exclusiveBadge : classNames.inclusiveBadge}>
          {isExclusionary ? ruleStrings.exclusive : ruleStrings.inclusive}
        </span>
        <span className={classNames.typeBadge}>{getTypeLabel()}</span>
      </div>
      <Text className={classNames.title}>{part.title}</Text>
      {detailRows.length > 0 && (
        <div className={classNames.detailRows}>
          {detailRows.map((row) => (
            <div className={classNames.detailRow} key={row.label}>
              <Text className={classNames.detailLabel}>{row.label}:</Text>
              {row.showPersona ? (
                <Persona text={row.value} size={PersonaSize.size24} />
              ) : (
                <Text className={classNames.detailValue}>{row.value}</Text>
              )}
            </div>
          ))}
        </div>
      )}
    </div>
  );
};
