import { h } from "preact";
import { effect, signal } from "@preact/signals";
import appstate, { RUNS_PAGE_LIMIT } from "../../appstate";
import { BackendURI } from "../../config";
import {
    IStoredRun, formatBytes, formatDate, formatDay, formatSimulatedSpan, formatWallTime,
    obstacleLabel, reachedHours, runBytes, trayLabel,
} from "../../helpers/StoredRun";
import {
    IStoreEvent, IStoreStatus, autoOpenLog, describeCounts, eventGlyph, recentCounts, shortRunId,
    visibleEvents, volumeLine,
} from "../../helpers/StoreLog";

/** The panel's two alarm colours, the same ones the rest of the HUD uses. Inline, not in
 *  index.css: a panel this small does not need a stylesheet of its own. */
const ERROR_COLOUR = '#ff5555';
const WARNING_COLOUR = '#ffaa33';

/**
 * The runs the server has kept, and what can be done with them.
 *
 * The HUD is a quarter of the window wide, so this is a list of cards rather than the table the
 * data suggests: one line of identity, one line of scale, and the rest behind a toggle. Every
 * number a row shows is one the server measured - a run that predates a measurement prints "?"
 * rather than a zero that would read as a count.
 */

/** Which row has its details open. One at a time; a page of open cards is unreadable. */
const expanded = signal('');
/** The row whose Delete has been clicked once. Deleting tens of gigabytes deserves a second click. */
const pendingDelete = signal('');
/** Extra simulated hours typed into a row's Continue box, per run. */
const extraHours = signal<Record<string, number>>({});
/** The row whose Continue has been clicked once. A continue is a full re-run; it gets a second click. */
const pendingContinue = signal('');
/** Whether the store log is open, once the user has said; null leaves it to `autoOpenLog`. */
const logOpen = signal<boolean | null>(null);
/** Routine events too - runs started, models stored. Deletions and problems always show. */
const showRoutine = signal(false);

/**
 * Re-reads the listing whenever the panel is opened.
 *
 * An effect rather than a fetch from inside the render: writing signals during a render that also
 * reads them is how a render loop starts, and the listing has to be re-read on every visit anyway.
 * A run started since the last look, or one that has finished, is exactly what a user opens this
 * panel to see. Most sessions never open it and pay nothing.
 */
effect(() => {
    if (appstate.activeTab.value === 'tab-runs') void appstate.refreshRuns();
});

export function RunsList() {
    const runs = appstate.runs.value;
    const total = runs.reduce((n, r) => n + runBytes(r), 0);
    const lineage = appstate.lineageParentRunId.value;
    //A full page means the store may hold more, and the total below then describes the page and
    //not the store. Worth saying: that total is what a user weighs before deleting anything.
    const capped = runs.length >= RUNS_PAGE_LIMIT;
    const status = appstate.storeStatus.value;
    //A disabled store answers the listing with nothing, exactly as an empty one does. Printing "No
    //stored runs" over it is how a full disk made a 56 GB run look deleted.
    const disabled = status !== null && !status.enabled;

    return <div>
        <div>
            <button onClick={() => appstate.refreshRuns()} disabled={appstate.runsLoading.value}
                    title="Re-read the list of stored runs from the server.">
                {appstate.runsLoading.value ? 'Loading…' : 'Refresh'}
            </button>
            &nbsp;<span title={capped
                    ? `The newest ${RUNS_PAGE_LIMIT} runs. The store may hold more, so this total is the total of what is listed.`
                    : 'Runs the server is keeping, and what they occupy in total.'}>
                {capped ? `newest ${runs.length}` : `${runs.length} run${runs.length === 1 ? '' : 's'}`}, {formatBytes(total)}
            </span>
        </div>

        <StoreBanner status={status} />

        {appstate.runsError.value.length > 0
            ? <div style={{ color: ERROR_COLOUR }} role="alert">{appstate.runsError.value}</div>
            : null}

        {/* Never invisible: the next run this tab starts will be recorded as a child of that run,
            and a lineage the user has forgotten setting is a lineage that lies. */}
        {lineage.length > 0
            ? <div title="The next simulation started from this tab will be recorded as derived from this run.">
                <small>
                    New runs derive from <code>{lineage.slice(0, 8)}</code>
                    {appstate.fieldModelRunId.value === lineage ? ' (its field model too)' : ''}
                    &nbsp;<button onClick={() => { appstate.lineageParentRunId.value = ''; appstate.fieldModelRunId.value = ''; }}
                            title="Start the next run as its own, with no recorded parent and no stored field model.">clear</button>
                </small>
            </div>
            : null}

        {runs.length === 0 && !appstate.runsLoading.value
            ? <p><small>{disabled
                ? 'The run store is disabled (see above), so no stored run can be listed and new runs are not kept.'
                : 'No stored runs. A simulation started here is kept by the server and appears in this list.'}</small></p>
            : null}

        <ul class="scrollable-listing">
            {runs.map(run => <li key={run.id}><RunRow run={run} /></li>)}
        </ul>

        {status ? <StoreLog status={status} /> : null}
    </div>;
}

/**
 * Whether the store works, and where it writes - above the list, because it decides how to read
 * the list.
 *
 * A disabled store is a red alert with the server's own reason. A working one is a single quiet line
 * - the volume, how full it is, the budget - plus a warning for every configuration that will lose
 * data, such as a budget larger than the disk or a store on an anonymous Docker volume. Those are
 * worked out by the server when asked, so they describe the store as it is now.
 */
function StoreBanner({ status }: { status: IStoreStatus | null }) {
    if (!status) return null;
    if (!status.enabled)
        return <div role="alert" style={{ color: ERROR_COLOUR, margin: '4px 0' }}>
            <b>The run store is disabled.</b> {status.disabledReason}
            <div><small>Runs are still computed and streamed here, but the server keeps none of them. Fix the cause, then restart the server.</small></div>
        </div>;
    return <div style={{ margin: '4px 0' }}>
        {status.warnings.map((warning, i) =>
            <div key={i} style={{ color: WARNING_COLOUR }}><small>⚠ {warning}</small></div>)}
        <div style={{ opacity: 0.75 }} title={`Run store root: ${status.root}`}>
            <small>{volumeLine(status)}</small>
        </div>
    </div>;
}

/**
 * What the store did and failed to do: every deletion and every storage error, newest first,
 * including those from before the server's last restart.
 *
 * Closed by default, so it costs a line; open by itself when the store is disabled or something
 * failed in the last day, because then it is the only account of what happened. Routine events
 * - a run started, a model stored - are one tick away and hidden otherwise.
 */
function StoreLog({ status }: { status: IStoreStatus }) {
    const now = Date.now();
    const routine = showRoutine.value;
    const open = logOpen.value ?? autoOpenLog(status, now);
    const shown = visibleEvents(status.events, routine);
    const counts = recentCounts(status.events, now, 7);
    return <details open={open} style={{ marginTop: '6px' }}
                    onToggle={e => { logOpen.value = (e.currentTarget as HTMLDetailsElement).open; }}>
        <summary title="Every deletion and every storage error the server recorded, newest first - including those from before its last restart.">
            Store log <small>· {describeCounts(counts)} in the last 7 days</small>
        </summary>
        <label title="Also show runs starting and finishing, models being stored and the store opening.">
            <input type="checkbox" checked={routine} onChange={e => { showRoutine.value = e.currentTarget.checked; }} />
            <small> routine events</small>
        </label>
        {shown.length === 0
            ? <p><small>Nothing recorded{routine ? '' : ' but routine events'}.</small></p>
            : <ul class="scrollable-listing" style={{ maxHeight: '40vh' }}>
                {shown.map(e => <li key={e.seq}><StoreEventRow event={e} /></li>)}
              </ul>}
    </details>;
}

function StoreEventRow({ event }: { event: IStoreEvent }) {
    const colour = event.level === 'error' ? ERROR_COLOUR : event.level === 'warning' ? WARNING_COLOUR : undefined;
    //Only a run still listed can be jumped to; the message names the others in full.
    const runId = event.runId ?? '';
    const listed = runId.length > 0 && appstate.runs.value.some(r => r.id === runId);
    return <div style={{ margin: '2px 0' }}>
        <small>
            <span title={`${event.utc} · ${event.kind}`}>{formatDate(event.utc)}</span>
            &nbsp;<span style={{ color: colour }} title={event.level}>{eventGlyph(event)}</span>
            {listed
                ? <span>&nbsp;<a href="#" title="Show this run's card." onClick={e => {
                        e.preventDefault();
                        expanded.value = runId;
                        document.getElementById(`run-${runId}`)?.scrollIntoView({ block: 'nearest' });
                    }}><code>{shortRunId(runId)}</code></a></span>
                : null}
            &nbsp;<span style={{ color: colour }}>{event.message}</span>
        </small>
    </div>;
}

function RunRow({ run }: { run: IStoredRun }) {
    const open = expanded.value === run.id;
    const busy = appstate.runsBusy.value === run.id;
    const shown = appstate.viewingRunId.value === run.id;
    const running = run.status === 'running';
    //A run can hold no frames at all - stopped before its first step - and there is nothing to
    //open. Saying so on the button beats an error message after the current scene has been cleared.
    const empty = run.storedFrames === 0;
    //Stopped before its horizon: aborted, or complete but short of the steps it was asked for.
    const unfinished = run.status !== 'running' && run.lastStep < run.totalSteps;

    //What a run reached against what it asked for. They differ whenever a run was stopped, and
    //that difference is the first thing worth knowing about an archived run.
    const asked = run.totalHours;
    const reached = reachedHours(run);
    const short = reached > 0 && asked > 0 && reached < asked;

    return <div class="runCard" id={`run-${run.id}`}>
        <div>
            <b>{formatDate(run.createdUtc)}</b>
            &nbsp;<span class={`runStatus runStatus-${run.status}`}>{run.status}</span>
            {run.deterministic
                ? <span title="Reproducible: re-running this request gives the same result, so a continuation genuinely continues it.">&nbsp;·&nbsp;det</span>
                : null}
        </div>
        <div title={`${run.storedFrames} stored frames of ${run.totalSteps} steps, ${run.hoursPerTick} h per tick.`}>
            <small>
                {formatSimulatedSpan(short ? reached : asked)}
                {short ? <span title={`Stopped early: asked for ${formatSimulatedSpan(asked)}.`}> of {formatSimulatedSpan(asked)}</span> : null}
                &nbsp;· {run.plants} plants · {trayLabel(run)} trays · {obstacleLabel(run)} obstacles
                &nbsp;· {formatBytes(runBytes(run))}
            </small>
        </div>
        {/* Why it stopped short, when it did. "aborted" alone sent people looking in the wrong
            place: a stop button, a sleeping laptop and a full disk all read the same without it. */}
        {run.stopReason
            ? <div style={{ color: WARNING_COLOUR }} title="Why this run stopped before its horizon.">
                <small>stopped: {run.stopReason}</small>
              </div>
            : null}

        <div>
            <button onClick={() => appstate.loadStoredRun(run.id)}
                    disabled={busy || appstate.computing.value || empty}
                    title={empty
                        ? 'This run stored no frames - it was stopped before its first step - so there is nothing to show.'
                        : run.hasTerrain
                            ? 'Open this run: its trays, its scenario and its whole timeline.'
                            : run.fieldModelName
                                //It used an imported model and neither the trays nor the model
                                //survive - the only case where opening it loses something.
                                ? `Open this run. Its field model (${run.fieldModelName}) is no longer stored, so its trays cannot be drawn unless you load that model yourself.`
                                //No model: nothing is missing. The viewport draws the configured
                                //field, which is exactly what this run showed while it computed.
                                : 'Open this run: its scenario and its whole timeline.'}>
                {shown ? 'Reopen' : 'View'}
            </button>
            <button onClick={() => appstate.forkStoredRun(run)} disabled={busy || appstate.computing.value}
                    title="Load this run's settings into the editor as the starting point for a new run. Nothing starts until you press Simulate.">
                Fork
            </button>
            {/* Only where it can actually happen. A run with no checkpoint - one stored before they
                were written - can still be forked, which recomputes it; saying "Resume" and then
                recomputing would be the wrong word for an hour of compute. */}
            {run.hasCheckpoint
                ? <button onClick={() => appstate.resumeStoredRun(run, resumeHoursFor(run))}
                          disabled={busy || appstate.computing.value}
                          title={unfinished
                              ? `Carry this run on from step ${run.lastStep}, where it stopped, without recomputing any of it.`
                              : `This run finished. Resuming extends it past the ${run.totalHours} h it was given, starting from its final state.`}>
                    {unfinished ? 'Resume' : 'Extend'}
                  </button>
                : null}
            <button onClick={() => expanded.value = open ? '' : run.id}
                    title="Everything else about this run.">
                {open ? '▴' : '▾'}
            </button>
            <button onClick={() => {
                        if (pendingDelete.peek() !== run.id) { pendingDelete.value = run.id; return; }
                        pendingDelete.value = '';
                        void appstate.deleteStoredRun(run);
                    }}
                    disabled={busy || running}
                    title={running
                        ? 'This run is still computing. Stop it before deleting it.'
                        : `Delete this run and its ${formatBytes(runBytes(run))} for good.`}>
                {pendingDelete.value === run.id ? `Delete ${formatBytes(runBytes(run))}?` : '🗑'}
            </button>
        </div>

        {open ? <RunDetails run={run} busy={busy} /> : null}
    </div>;
}

/**
 * How much simulated time a Resume should add.
 *
 * For a run that stopped short, none: finishing it means reaching the total it already has, and
 * asking the user for a number to complete something they already asked for is a question with one
 * sensible answer. For a run that finished, the box in the details panel decides - extending is a
 * new decision and needs one.
 */
function resumeHoursFor(run: IStoredRun): number {
    if (run.lastStep < run.totalSteps) return 0;
    return extraHours.value[run.id] ?? 24;
}

function RunDetails({ run, busy }: { run: IStoredRun, busy: boolean }) {
    //One day, not the run's own length. A continue re-runs the whole scenario from step zero and
    //then keeps going, so the cheapest useful answer is the right default - prefilling the run's
    //own 2160 h made a single unconfirmed click cost a 180-day simulation.
    const extra = extraHours.value[run.id] ?? 24;
    const canContinue = !busy && !appstate.computing.value;
    const armed = pendingContinue.value === run.id;

    return <div class="runDetails">
        <small>
            <div><code>{run.id}</code></div>
            <div>
                seed {run.seed} · {run.hoursPerTick} h/tick · {run.storedFrames}/{run.totalSteps} frames
                · {run.species} species · render mode {run.renderMode}
            </div>
            <div>starts {formatDay(run.initialDay)} · computed in {formatWallTime(run)}</div>
            <div>frames {formatBytes(run.bytesOnDisk)} · rest {formatBytes(run.auxBytesOnDisk)}</div>
            {run.fieldModelName
                ? <div title={run.hasFieldModel
                        ? 'The model is stored, so this run draws its trays and can be continued or forked without uploading it again.'
                        : 'The model is no longer stored. This run cannot be continued or forked from it, and its trays cannot be drawn - upload the OBJ again first.'}>
                    model {run.fieldModelName} {run.hasFieldModel ? '✓' : '(gone)'}
                  </div>
                : null}
            {run.parentRunId
                ? <div title="This run was continued or forked from another.">
                    derived from <code>{run.parentRunId.slice(0, 8)}</code>
                  </div>
                : null}
            {run.hasObstacleLight
                ? <div>
                    <a href={`${location.protocol}//${BackendURI}/simulation/run/${run.id}/obstaclelight`}
                       title="The complete leaf area index document: weekly, annual and whole-run readings of every measured obstacle and the foliage of every tray.">
                      download the leaf area index
                    </a>
                    {' '}
                    <a href={`${location.protocol}//${BackendURI}/simulation/run/${run.id}/obstaclelight/packed`}
                       download={`leafareaindex-${run.id}.json.gz`}
                       title="The same document as it is stored, gzipped.">
                      (.json.gz)
                    </a>
                  </div>
                : null}

            {run.hasCheckpoint
                ? <div title="A checkpoint of the live simulation, written as the run went. It is what lets the run be carried on instead of computed again.">
                    resumable from step {run.lastStep}
                  </div>
                : <div title="Runs stored before checkpoints existed, or whose checkpoint could not be written, can only be recomputed.">
                    no checkpoint - can only be forked
                  </div>}

            {/* Continue is a RE-RUN and the wording has to say so. Nothing here stores live world
                state, so the extension is computed from step zero - exactly reproducing the parent
                when it was deterministic, and merely resembling it when it was not. */}
            <div title={run.deterministic
                    ? 'Re-runs this scenario from the start up to the new total. The parent was deterministic, so the first part reproduces it exactly.'
                    : 'Re-runs this scenario from the start up to the new total. The parent was NOT deterministic, so this will not reproduce it - it is a second sample of the same setup.'}>
                continue to {run.totalHours} +
                <input type="number" min={0} max={100000} step={24} value={extra}
                       style={{ width: '5em' }}
                       onChange={e => extraHours.value = { ...extraHours.peek(), [run.id]: parseInt(e.currentTarget.value, 10) || 0 }} />
                h
                {/* Two clicks, like Delete. A continue starts a full re-run of the parent plus the
                    extension the moment it is pressed, which at this scale is hours of compute. */}
                <button disabled={!canContinue}
                        onClick={() => {
                            if (!armed) { pendingContinue.value = run.id; return; }
                            pendingContinue.value = '';
                            void appstate.continueStoredRun(run, extra);
                        }}>
                    {armed ? `Re-run ${formatSimulatedSpan(run.totalHours + extra)}?` : 'Continue'}
                </button>
                {run.deterministic ? '' : ' ⚠ not reproducible'}
            </div>
        </small>
    </div>;
}
