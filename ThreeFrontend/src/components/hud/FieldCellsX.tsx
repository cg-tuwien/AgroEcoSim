import { Component, h } from "preact";
import appstate from "../../appstate";
//import "wired-elements"

const min = 1;
const max = 100000;

export class FieldCellsX extends Component
{
    render() {
        return <div>
            <input min={min} max={max} type="number" name="fieldcellsx" value={appstate.fieldCellsX} onChange={e => {
                let step = 1;
                if (appstate.fieldCellsX.value >= 1000) step = 1000;
                else if (appstate.fieldCellsX.value >= 100) step = 100;
                else if (appstate.fieldCellsX.value >= 20) step = 10;
                else step = 1;

                if (parseInt(e.currentTarget.value) > appstate.fieldCellsX.value)
                    appstate.fieldCellsX.value = Math.round(Math.min(max, appstate.fieldCellsX.value + step));
                else
                    appstate.fieldCellsX.value = Math.round(Math.max(min, appstate.fieldCellsX.value - step));
            }}/>
            <label for="fieldcellsx">Width of the field (in meters)</label>
        </div>;
    }
}