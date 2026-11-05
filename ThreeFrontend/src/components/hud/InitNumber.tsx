import { Component, h } from "preact";
import appstate from "../../appstate";
//import "wired-elements"

export function InitNumber() {
    return <div>
        <input type="text" inputMode="numeric" name="initNumber" value={appstate.initNumber.value} onChange={e => appstate.initNumber.value = e.currentTarget.value} disabled={appstate.randomize.value} />
        <label for="initNumber">Initial random number that controls the simulation</label>
    </div>;
}
