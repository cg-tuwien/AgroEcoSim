import { Component, h, Fragment } from "preact";
import appstate from "../../appstate";

export class ProgressBar extends Component
{
    render() {
        return appstate.simLength.value > 0
            ? <span>{appstate.simStep} / {appstate.simLength} ({(100.0 * appstate.simStep.value/appstate.simLength.value).toFixed(2)}%)</span>
            : <></>;
    }
}