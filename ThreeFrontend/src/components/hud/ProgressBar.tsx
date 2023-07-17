import { Component, h, Fragment } from "preact";
import appstate from "../../appstate";

export function ProgressBar() {
    return <>
        <span title="Memory needed to store the simulation result (all frames).">{Math.round(appstate.historySize.value / 1048576)} Mb</span>&nbsp;
        {appstate.simLength.value > 0
        ? <span title="Computation progress (slows down as plants grow bigger).">{appstate.simStep} / {appstate.simLength} ({(100.0 * appstate.simStep.value/appstate.simLength.value).toFixed(2)}%)</span>
        : <></>}
    </>;
}