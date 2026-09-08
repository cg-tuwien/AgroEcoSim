import { Component, h, Fragment } from "preact";
import appstate from "../../appstate";

export function ProgressBar() {
    const used = appstate.historySize.value;
    const budget = appstate.historyBudget.value;
    //Colour before the number matters: the figure alone tells nobody whether 1500 Mb is fine.
    const share = budget > 0 ? used / budget : 0;
    const colour = appstate.historyLimitHit.value || share >= 1 ? '#ff5555'
        : share > 0.75 ? '#ffaa33'
        : undefined;

    return <>
        <span style={{ color: colour }}
            title={appstate.historyLimitHit.value
                ? `Stopped: the stored frames reached the ${(budget / 1048576).toFixed(0)} Mb this tab can hold. `
                  + `What was computed is intact and can still be saved or exported.`
                : `Memory held by the stored frames, out of the ~${(budget / 1048576).toFixed(0)} Mb `
                  + `this tab can spare. `
                  + (appstate.runId.value
                      ? `Past that, older frames are handed to the server and fetched back on demand.`
                      : `Past that the run stops, because with no server-side store an older frame `
                        + `dropped here would be gone for good.`)}>
            {+(used / 1048576).toFixed(3)} Mb
            {share > 0.75 ? ` / ${(budget / 1048576).toFixed(0)}` : ''}
            {appstate.historyLimitHit.value ? ' — full, run stopped' : ''}
        </span>&nbsp;
        {/* Frames the client no longer holds. Worth showing: it is the difference between
            "the run is fine, older frames come from the server" and something being lost. */}
        {appstate.evictedFrames.value > 0
            ? <span title="Older frames are held by the server and fetched back when you scrub to them."
                >({appstate.evictedFrames.value} streamed)</span>
            : null}&nbsp;
        {appstate.simLength.value > 0
        ? <span title="Computation progress (slows down as plants grow bigger).">{appstate.simStep.value} / {appstate.simLength.value} ({(100.0 * appstate.simStep.value/appstate.simLength.value).toFixed(2)}%)</span>
        : <></>}
    </>;
}