// jest-dom adds custom jest matchers for asserting on DOM nodes.
// allows you to do things like:
// expect(element).toHaveTextContent(/react/i)
// learn more: https://github.com/testing-library/jest-dom
import '@testing-library/jest-dom';
import { vi } from 'vitest';

// Ensure localStorage is available in all test environments
if (typeof globalThis.localStorage === 'undefined' || typeof globalThis.localStorage.getItem !== 'function') {
  const storage: Record<string, string> = {};
  Object.defineProperty(globalThis, 'localStorage', {
    value: {
      getItem: (key: string) => storage[key] ?? null,
      setItem: (key: string, value: string) => { storage[key] = value; },
      removeItem: (key: string) => { delete storage[key]; },
      clear: () => { for (const k of Object.keys(storage)) delete storage[k]; },
      key: (_i: number) => null,
      length: 0,
    },
    configurable: true,
  });
}

const jestCompat = {
	...vi,
	requireActual: (modulePath: string) => {
		return require(modulePath);
	},
};

(globalThis as any).jest = jestCompat;

jest.mock('@azure/msal-browser', () => {
	const mockAccount = {
		localAccountId: 'test-account-id',
		name: 'Test User',
		username: 'test.user@example.com',
	};

	const acquireTokenSilent = jest
		.fn()
		.mockResolvedValue({ accessToken: 'test-access-token' });

	// Mirrors @azure/msal-browser's error raised by acquireTokenSilent when the
	// cached session can no longer be used silently. MsalAuthenticationService
	// relies on `instanceof InteractionRequiredAuthError`, so the mock must
	// export a real class.
	class InteractionRequiredAuthError extends Error {
		constructor(message?: string) {
			super(message);
			this.name = 'InteractionRequiredAuthError';
			Object.setPrototypeOf(this, InteractionRequiredAuthError.prototype);
		}
	}

	class MockPublicClientApplication {
		initialize = jest.fn().mockResolvedValue(undefined);
		handleRedirectPromise = jest.fn().mockResolvedValue(null);
		getAllAccounts = jest.fn().mockReturnValue([mockAccount]);
		loginRedirect = jest.fn().mockResolvedValue(undefined);
		setActiveAccount = jest.fn();
		getActiveAccount = jest.fn().mockReturnValue(mockAccount);
		acquireTokenSilent = acquireTokenSilent;
		acquireTokenRedirect = jest.fn().mockResolvedValue(undefined);
	}

	return {
		PublicClientApplication: MockPublicClientApplication,
		InteractionRequiredAuthError,
	};
});
