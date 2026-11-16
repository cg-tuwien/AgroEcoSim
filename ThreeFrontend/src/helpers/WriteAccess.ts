import { batch, signal } from '@preact/signals';
import { BackendURI } from '../config';

export const WRITE_PASSWORD_HEADER = 'X-Agro-Write-Password';
const LOCKED_MESSAGE = 'Unlock writes with the shared password.';

/** Credentials live in this closure only, outside run settings, archives and browser storage. */
export function createWriteAccess(baseUrl: string) {
    let password = '';
    let generation = 0;
    const lockedListeners = new Set<() => void>();
    const unlocked = signal(false);
    const busy = signal(false);
    const error = signal('');

    function lock(message = '') {
        ++generation;
        password = '';
        batch(() => { unlocked.value = false; busy.value = false; error.value = message; });
        for (const listener of lockedListeners) listener();
    }

    function requirePassword() {
        if (!unlocked.peek() || !password) {
            error.value = LOCKED_MESSAGE;
            throw new Error(LOCKED_MESSAGE);
        }
        return password;
    }

    async function unlock(candidate: string): Promise<boolean> {
        const attempt = ++generation;
        batch(() => { busy.value = true; error.value = ''; });
        try {
            if (!candidate) throw new Error('Enter the shared password.');
            const response = await fetch(`${baseUrl}/simulation/write-access`, {
                method: 'POST', headers: { [WRITE_PASSWORD_HEADER]: candidate },
            });
            if (!response.ok) throw new Error(response.status === 401 || response.status === 403
                ? 'Incorrect write password.'
                : response.status === 404 ? 'This server does not support protected writes. Writes remain locked.'
                : `Could not unlock writes (HTTP ${response.status}).`);
            if (attempt !== generation) return false;
            password = candidate;
            unlocked.value = true;
            return true;
        } catch (failure) {
            if (attempt === generation) lock(failure instanceof Error ? failure.message : 'Could not unlock writes.');
            return false;
        } finally {
            if (attempt === generation) busy.value = false;
        }
    }

    async function writeFetch(url: string, init: Omit<RequestInit, 'headers'> & { headers?: Record<string, string> }): Promise<Response> {
        const credential = requirePassword();
        const response = await fetch(url, {
            ...init, headers: { ...init.headers, [WRITE_PASSWORD_HEADER]: credential },
        });
        if (!response.ok) {
            if (response.status === 401 || response.status === 403) {
                lock(LOCKED_MESSAGE);
                throw new Error(LOCKED_MESSAGE);
            }
            const detail = await response.text();
            if (/WRITE_ACCESS_(REQUIRED|DISABLED)/.test(detail)) lock(LOCKED_MESSAGE);
            throw new Error(detail || `The server refused the write (HTTP ${response.status}).`);
        }
        return response;
    }

    async function invoke<T>(hub: { invoke<T>(method: string, ...args: any[]): Promise<T> }, method: string, ...args: any[]): Promise<T> {
        const credential = requirePassword();
        try {
            return await hub.invoke<T>(method, ...args, credential);
        } catch (failure) {
            if (/WRITE_ACCESS_(REQUIRED|DISABLED)/.test(String(failure))) lock(LOCKED_MESSAGE);
            throw failure;
        }
    }

    return {
        unlocked, busy, error, unlock, lock, requirePassword, writeFetch, invoke,
        onLock(listener: () => void) { lockedListeners.add(listener); return () => { lockedListeners.delete(listener); }; },
    };
}

export const writeAccess = createWriteAccess(`${location.protocol}//${BackendURI}`);
