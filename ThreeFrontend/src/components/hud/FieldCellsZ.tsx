import { Component, h } from "preact";
import appstate from "../../appstate";
//import "wired-elements"

const min = 1;
const max = 100000;

export class FieldCellsZ extends Component
{
    render() {
        return <div>
            <input min={min} max={max} type="number" name="fieldcellsz" value={appstate.fieldCellsZ} onChange={e => {
                let step = 1;
                if (appstate.fieldCellsZ.value >= 1000) step = 1000;
                else if (appstate.fieldCellsZ.value >= 100) step = 100;
                else if (appstate.fieldCellsZ.value >= 20) step = 10;
                else step = 1;

                if (parseInt(e.currentTarget.value) > appstate.fieldCellsZ.value)
                    appstate.fieldCellsZ.value = Math.round(Math.min(max, appstate.fieldCellsZ.value + step));
                else
                    appstate.fieldCellsZ.value = Math.round(Math.max(min, appstate.fieldCellsZ.value - step));
            }}/>
            <label for="fieldcellsz">Length of the field (in meters)</label>
        </div>;
    }
}