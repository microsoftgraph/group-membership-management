// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.

import { chromium, expect, FullConfig } from '@playwright/test';

const EMAIL = process.env.INTEGRATION_TEST_EMAIL || '';
const PASSWORD = process.env.INTEGRATION_TEST_PASSWORD || '';
const DOMAIN = process.env.INTEGRATION_TEST_DOMAIN || '';

async function globalSetup(config: FullConfig) {
  if (!EMAIL || !PASSWORD || !DOMAIN) {
    console.error('❌ Environment variables for email, password, or domain are not set.');
    process.exit(1);
  }

  console.log('✅ Starting authentication setup...');

  const browser = await chromium.launch({ headless: true });
  const context = await browser.newContext();
  const page = await context.newPage();

  try {
    console.log('🔗 Navigating to login page...');
    const url = DOMAIN.startsWith('http://') || DOMAIN.startsWith('https://') ? DOMAIN : `https://${DOMAIN}`;
    await page.goto(url);

    console.log('📝 Filling in login details...');
    await page.getByRole('textbox', { name: 'Enter your email, phone, or' }).fill(EMAIL);
    await page.getByRole('button', { name: 'Next' }).click();

    await page.getByRole('textbox', { name: 'Enter the password for' }).fill(PASSWORD);
    await page.getByRole('button', { name: 'Sign in' }).click();

    const displaySignContainer = await page.locator('.display-sign-container');
    const textContent = await displaySignContainer.innerText();
    console.log(`Please tap the number ${textContent} on your phone`);

    await page.getByText("Don't show this again").click();
    await page.getByRole('button', { name: 'Yes' }).click();

    // Check if the disclaimer modal is visible and submit it if needed
    const modal = page.locator('#disclaimerModal');
    if (await modal.isVisible()) {
      // Check all checkboxes in the disclaimer modal
      const checkboxes = page.locator('#disclaimerModal .ms-Checkbox-checkbox');
      const checkboxCount = await checkboxes.count();
      for (let i = 0; i < checkboxCount; i++) {
        const checkbox = checkboxes.nth(i);
        // Find the corresponding input to check if it's already checked
        const input = page.locator('#disclaimerModal input[type="checkbox"]').nth(i);
        if (!(await input.isChecked())) {
          await checkbox.click();
        }
      }
      // Click the submit button by ID
      const submitButton = page.locator('#disclaimerSubmitButton');
      await expect(submitButton).toBeEnabled();
      await submitButton.click();
      // Explicitly set disclaimerSubmitted in localStorage to ensure persistence
      await page.evaluate(() => {
        localStorage.setItem('disclaimerSubmitted', 'true');
      });
    } else {
      // If modal is not visible, still ensure disclaimerSubmitted is set
      await page.evaluate(() => {
        localStorage.setItem('disclaimerSubmitted', 'true');
      });
    }

    await expect(page.locator('text="Membership Management"')).toBeVisible({ timeout: 10000 });

    console.log('⏳ Waiting for dashboard to load...');
    await page.waitForSelector('text="Membership Management"');

    console.log('💾 Saving authentication state...');
    await context.storageState({ path: 'tests/storageState.json' });

    console.log('✅ Auth state saved to storageState.json');
  } catch (error) {
    console.error('❌ Authentication setup failed:', error);
  } finally {
    await browser.close();
  }
}

export default globalSetup;
