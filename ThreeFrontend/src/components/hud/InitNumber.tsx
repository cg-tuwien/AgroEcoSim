import { Component, h } from "preact";
import appstate from "../../appstate";
//import "wired-elements"

export function InitNumber() {
    return <div>
        <input min={1} type="number" name="initNumber" value={appstate.initNumber} onChange={e => appstate.initNumber.value = parseInt(e.currentTarget.value)} disabled={appstate.randomize.value} />
        <label for="initNumber">Initial random number that controls the simulation</label>
    </div>;
}