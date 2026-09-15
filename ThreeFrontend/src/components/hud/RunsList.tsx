import { h } from "preact";
import { effect, signal } from "@preact/signals";
import appstate, { RUNS_PAGE_LIMIT } from "../../appstate";
import { BackendURI } from "../../config";
import {
    IStoredRun, formatBytes, formatDate, formatDay, formatSimulatedSpan, formatWallTime,
    obstacleLabel, reachedHours, runBytes, trayLabel,
} from "../../helpers/StoredRun";

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

        {appstate.runsError.value.length > 0
            ? <div style={{ color: '#ff5555' }} role="alert">{appstate.runsError.value}</div>
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
            ? <p><small>No stored runs. A simulation started here is kept by the server and appears in this list.</small></p>
            : null}

        <ul class="scrollable-listing">
            {runs.map(run => <li key={run.id}><RunRow run={run} /></li>)}
        </ul>
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

    //What a run reached against what it asked for. They differ whenever a run was stopped, and
    //that difference is the first thing worth knowing about an archived run.
    const asked = run.totalHours;
    const reached = reachedHours(run);
    const short = reached > 0 && asked > 0 && reached < asked;

    return <div class="runCard">
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
                ? <div><a href={`${location.protocol}//${BackendURI}/simulation/run/${run.id}/obstaclelight`}
                          title="The complete leaf area index measurement, with every per-obstacle daily series.">
                    download the full leaf area index
                  </a></div>
                : null}

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
