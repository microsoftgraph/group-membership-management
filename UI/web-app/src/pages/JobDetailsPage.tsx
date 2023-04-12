import React from "react";
import { Page } from "../components/Page";
import { useLocation } from "react-router-dom";
import { Text } from "@fluentui/react";
import { Job } from "../models/Job";
import { PageHeader } from "../components/PageHeader/PageHeader";
import {
  Stack,
  IStackStyles,
  IStackTokens,
  IStackItemStyles,
} from "@fluentui/react/lib/Stack";


const stackItemStyles: IStackItemStyles = {
  root: {
    fontSize: 15,
    padding: 5,
  },
};

const titleStackItemStyles: IStackItemStyles = {
  root: {
    fontSize: 15,
    fontWeight: "bold",
  },
};

const itemAlignmentsStackTokens: IStackTokens = {
  childrenGap: 5,
  padding: 10,
};


export const JobDetailsPage: React.FunctionComponent = () => {
  const location = useLocation();
  const job: Job = location.state.item;


  const stackStyles: Partial<IStackStyles> = { root: { height: 44 } };
  return (
    <Page>

      <PageHeader/>

      <Text variant="xxLarge">Membership Details</Text>

      <Stack
        enableScopedSelectors
        styles={stackStyles}
        tokens={itemAlignmentsStackTokens}
      >
        <Stack enableScopedSelectors tokens={itemAlignmentsStackTokens}>
          <Stack.Item align="start" styles={titleStackItemStyles}>
            <span>Identifier</span>
          </Stack.Item>
          <Stack.Item align="start" styles={stackItemStyles}>
            <span>{job.targetGroupId}</span>
          </Stack.Item>
        </Stack>

        <Stack enableScopedSelectors tokens={itemAlignmentsStackTokens}>
          <Stack.Item align="start" styles={titleStackItemStyles}>
            <span>Object Type</span>
          </Stack.Item>
          <Stack.Item align="start" styles={stackItemStyles}>
            <span>{job.targetGroupType}</span>
          </Stack.Item>
        </Stack>

        <Stack enableScopedSelectors tokens={itemAlignmentsStackTokens}>
          <Stack.Item align="start" styles={titleStackItemStyles}>
            <span>Initial Onboarding Time</span>
          </Stack.Item>
          <Stack.Item align="start" styles={stackItemStyles}>
            <span>{job.startDate}</span>
          </Stack.Item>
        </Stack>

        <Stack enableScopedSelectors tokens={itemAlignmentsStackTokens}>
          <Stack.Item align="start" styles={titleStackItemStyles}>
            <span>Last Successful Start Time</span>
          </Stack.Item>
          <Stack.Item align="start" styles={stackItemStyles}>
            <span>{job.lastSuccessfulStartTime}</span>
          </Stack.Item>
        </Stack>

        <Stack enableScopedSelectors tokens={itemAlignmentsStackTokens}>
          <Stack.Item align="start" styles={titleStackItemStyles}>
            <span>Last Successful Run Time</span>
          </Stack.Item>
          <Stack.Item align="start" styles={stackItemStyles}>
            <span>{job.lastSuccessfulRunTime}</span>
          </Stack.Item>
        </Stack>

        <Stack enableScopedSelectors tokens={itemAlignmentsStackTokens}>
          <Stack.Item align="start" styles={titleStackItemStyles}>
            <span>Estimated Next Run Time</span>
          </Stack.Item>
          <Stack.Item align="start" styles={stackItemStyles}>
            <span>{job.estimatedNextRunTime}</span>
          </Stack.Item>
        </Stack>

        <Stack enableScopedSelectors tokens={itemAlignmentsStackTokens}>
          <Stack.Item align="start" styles={titleStackItemStyles}>
            <span>Threshold Increase</span>
          </Stack.Item>
          <Stack.Item align="start" styles={stackItemStyles}>
            <span>{job.thresholdPercentageForAdditions}</span>
          </Stack.Item>
        </Stack>

        <Stack enableScopedSelectors tokens={itemAlignmentsStackTokens}>
          <Stack.Item align="start" styles={titleStackItemStyles}>
            <span>Threshold Decrease</span>
          </Stack.Item>
          <Stack.Item align="start" styles={stackItemStyles}>
            <span>{job.thresholdPercentageForRemovals}</span>
          </Stack.Item>
        </Stack>
      </Stack>
    </Page>
  );
};
