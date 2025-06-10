// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { Page, Browser } from '@playwright/test';
import { SettingKey, SettingKeyMap } from '../../src/models';

export async function ensureGroupCreationEnabled(browser: Browser, domain: string): Promise<void> {
  const context = await browser.newContext({ storageState: 'tests/storageState.json' });
  const page = await context.newPage();
  
  try {
    const url = domain.startsWith('http://') || domain.startsWith('https://') ? domain : `https://${domain}`;
    await page.goto(`${url}/Admin`);
    await page.locator('text="General"').click();
    
    const groupCreationToggleId = SettingKeyMap[SettingKey.CreateGroupFeatureEnabled];
    const isGroupCreationEnabled = await page.locator(`#${groupCreationToggleId}`).isChecked();
    
    if (!isGroupCreationEnabled) {
      console.log('⚙️ Enabling group creation feature...');
      await page.locator(`#${groupCreationToggleId}`).click();
      await page.locator('text="Save"').click();
      await page.waitForTimeout(5000);
      console.log('✅ Group creation feature enabled.');
    } else {
      console.log('✅ Group creation feature is already enabled.');
    }
  } catch (error) {
    console.error('❌ Failed to enable group creation feature:', error);
    throw error;
  } finally {
    await context.close();
  }
}

export async function ensureDisclaimerEnabled(browser: Browser, domain: string): Promise<void> {
  const context = await browser.newContext({ storageState: 'tests/storageState.json' });
  const page = await context.newPage();
  
  try {
    const url = domain.startsWith('http://') || domain.startsWith('https://') ? domain : `https://${domain}`;
    await page.goto(`${url}/Admin`);
    await page.locator('text="General"').click();
    
    const disclaimerToggleId = SettingKeyMap[SettingKey.IsDisclaimerEnabled];
    const isDisclaimerEnabled = await page.locator(`#${disclaimerToggleId}`).isChecked();
    
    if (!isDisclaimerEnabled) {
      console.log('⚙️ Enabling disclaimer...');
      await page.locator(`#${disclaimerToggleId}`).click();
      await page.locator('text="Save"').click();
      await page.waitForTimeout(5000);
      console.log('✅ Disclaimer enabled.');
    } else {
      console.log('✅ Disclaimer is already enabled.');
    }
  } catch (error) {
    console.error('❌ Failed to enable disclaimer:', error);
    throw error;
  } finally {
    await context.close();
  }
}
