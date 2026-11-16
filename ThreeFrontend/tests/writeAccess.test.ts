import { createWriteAccess, WRITE_PASSWORD_HEADER } from '../src/helpers/WriteAccess';

const reply = (status = 204, body = '') => ({ ok: status >= 200 && status < 300, status, text: async () => body }) as Response;

beforeEach(() => {
    sessionStorage.clear(); localStorage.clear();
    global.fetch = jest.fn(async () => reply()) as any;
});

test('writes fail closed until a server accepts the password, including older servers', async () => {
    const access = createWriteAccess('https://example.test');
    await expect(access.writeFetch('/simulation/upload', { method: 'POST' })).rejects.toThrow('Unlock writes');
    expect(global.fetch).not.toHaveBeenCalled();
    (global.fetch as jest.Mock).mockResolvedValue(reply(404));
    expect(await access.unlock('test-only-secret')).toBe(false);
    expect(access.unlocked.peek()).toBe(false);
    expect(access.error.peek()).toContain('does not support');
    (global.fetch as jest.Mock).mockResolvedValue(reply(401));
    expect(await access.unlock('wrong')).toBe(false);
    expect(access.error.peek()).toBe('Incorrect write password.');
});

test('credentials travel in write headers and the final hub argument, never URLs or serialized state', async () => {
    const access = createWriteAccess('https://example.test');
    expect(await access.unlock('test-only-secret')).toBe(true);
    expect(global.fetch).toHaveBeenLastCalledWith('https://example.test/simulation/write-access', {
        method: 'POST', headers: { [WRITE_PASSWORD_HEADER]: 'test-only-secret' },
    });
    await access.writeFetch('/simulation/run/a/protection', {
        method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: '{"protected":true}',
    });
    expect(global.fetch).toHaveBeenLastCalledWith('/simulation/run/a/protection', {
        method: 'PUT', body: '{"protected":true}',
        headers: { 'Content-Type': 'application/json', [WRITE_PASSWORD_HEADER]: 'test-only-secret' },
    });
    const hub = { invoke: jest.fn(async () => true) };
    await access.invoke(hub as any, 'terrain', 'terrain-id', 4, '_S', true, 0.1);
    expect(hub.invoke).toHaveBeenCalledWith('terrain', 'terrain-id', 4, '_S', true, 0.1, 'test-only-secret');
    expect(JSON.stringify(access)).not.toContain('test-only-secret');
    expect(sessionStorage.length).toBe(0);
    expect(localStorage.length).toBe(0);
    access.lock();
    await expect(access.invoke(hub as any, 'abort')).rejects.toThrow('Unlock writes');
    expect(hub.invoke).toHaveBeenCalledTimes(1);
});

test('HTTP and hub authorization rejection relock and notify the live client to detach', async () => {
    const access = createWriteAccess('https://example.test');
    const detached = jest.fn();
    access.onLock(detached);
    await access.unlock('test-only-secret');
    (global.fetch as jest.Mock).mockResolvedValue(reply(401, 'WRITE_ACCESS_REQUIRED'));
    await expect(access.writeFetch('/simulation/upload', { method: 'POST' })).rejects.toThrow('Unlock writes');
    expect(access.unlocked.peek()).toBe(false);
    expect(detached).toHaveBeenCalledTimes(1);
    (global.fetch as jest.Mock).mockResolvedValue(reply());
    await access.unlock('test-only-secret');
    const hub = { invoke: jest.fn(async () => { throw new Error('WRITE_ACCESS_DISABLED: unavailable'); }) };
    await expect(access.invoke(hub as any, 'preview')).rejects.toThrow('WRITE_ACCESS_DISABLED');
    expect(access.unlocked.peek()).toBe(false);
    expect(detached).toHaveBeenCalledTimes(2);
});

test('locking cancels a pending unlock even if its server answer arrives later', async () => {
    const access = createWriteAccess('https://example.test');
    let resolve!: (response: Response) => void;
    (global.fetch as jest.Mock).mockImplementation(() => new Promise<Response>(done => { resolve = done; }));
    const pending = access.unlock('test-only-secret');
    access.lock();
    resolve(reply());
    expect(await pending).toBe(false);
    expect(access.unlocked.peek()).toBe(false);
    expect(access.busy.peek()).toBe(false);
});
