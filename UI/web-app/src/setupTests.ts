// jest-dom adds custom jest matchers for asserting on DOM nodes.
// allows you to do things like:
// expect(element).toHaveTextContent(/react/i)
// learn more: https://github.com/testing-library/jest-dom
import '@testing-library/jest-dom';
import { vi } from 'vitest';

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

	class MockPublicClientApplication {
		initialize = jest.fn().mockResolvedValue(undefined);
		handleRedirectPromise = jest.fn().mockResolvedValue(null);
		getAllAccounts = jest.fn().mockReturnValue([mockAccount]);
		loginRedirect = jest.fn().mockResolvedValue(undefined);
		setActiveAccount = jest.fn();
		getActiveAccount = jest.fn().mockReturnValue(mockAccount);
		acquireTokenSilent = acquireTokenSilent;
	}

	return {
		PublicClientApplication: MockPublicClientApplication,
	};
});
