import { Component, h } from "preact";
import appstate from "../../appstate";
//import "wired-elements"

const min = 0.1;
const max = 1000;

export function FieldCellsD() {
    //const tooltiup = "Basic depth of the field. The height field adds soil on top of it.";
    return <div>
        <input min={min} max={max} type="number" name="fieldcellsd" value={appstate.fieldCellsD} onChange={e => {
            let step = 1;
            if (appstate.fieldCellsD.value >= 100) step = 100;
            else if (appstate.fieldCellsD.value >= 20) step = 10;
            else if (appstate.fieldCellsD.value >= 2) step = 1;
            else step = 0.1;

            if (parseInt(e.currentTarget.value) > appstate.fieldCellsD.value)
                appstate.fieldCellsD.value = Math.round(Math.min(max, appstate.fieldCellsD.value + step));
            else
                appstate.fieldCellsD.value = Math.round(Math.max(min, appstate.fieldCellsD.value - step));
        }}/>
        <label for="fieldcellsd">Depth of the soil (in meters)</label>
    </div>;
}