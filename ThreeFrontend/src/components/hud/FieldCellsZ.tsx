import { Component, h } from "preact";
import appstate from "../../appstate";
//import "wired-elements"

const min = 1;
const max = 100000;

export function FieldSizeZ() {
    return <div>
        <input min={min} max={max} type="number" name="fieldSizeZ" value={appstate.fieldSizeZ} onChange={e => {
            let step = 1;
            if (appstate.fieldSizeZ.value >= 1000) step = 1000;
            else if (appstate.fieldSizeZ.value >= 100) step = 100;
            else if (appstate.fieldSizeZ.value >= 20) step = 10;
            else step = 1;

            if (parseInt(e.currentTarget.value) > appstate.fieldSizeZ.value)
                appstate.fieldSizeZ.value = Math.round(Math.min(max, appstate.fieldSizeZ.value + step));
            else
                appstate.fieldSizeZ.value = Math.round(Math.max(min, appstate.fieldSizeZ.value - step));
        }}/>
        <label for="fieldSizeZ">Length of the field (in meters)</label>
    </div>;
}