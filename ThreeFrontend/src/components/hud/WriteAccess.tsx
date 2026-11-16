import { h } from 'preact';
import { useState } from 'preact/hooks';
import { writeAccess } from '../../helpers/WriteAccess';

/** The shared password is entered here, never shipped in the application or saved with a run. */
export function WriteAccess({ actionError = '' }: { actionError?: string }) {
    const [candidate, setCandidate] = useState('');
    return <div style={{ margin: '4px 0' }}>
        {writeAccess.unlocked.value
            ? <div><b>Writes unlocked</b>{' '}<button onClick={() => writeAccess.lock()}>Lock</button></div>
            : <form onSubmit={async event => {
                event.preventDefault();
                const password = candidate;
                setCandidate('');
                await writeAccess.unlock(password);
            }}>
                <b>Read-only</b>{' '}
                <input type="password" aria-label="Shared write password" autoComplete="off" value={candidate}
                       placeholder="Shared password" disabled={writeAccess.busy.value}
                       onInput={event => setCandidate(event.currentTarget.value)} style={{ width: '9em' }} />{' '}
                <button type="submit" disabled={writeAccess.busy.value}>{writeAccess.busy.value ? 'Checking…' : 'Unlock writes'}</button>
            </form>}
        {writeAccess.error.value ? <div role="alert">{writeAccess.error.value}</div> : null}
        {actionError && !writeAccess.error.value ? <div role="alert">{actionError}</div> : null}
    </div>;
}
