import { Component, h } from "preact";
import appstate from "../../appstate";
//import "wired-elements"

const min = 1;
const max = 100000;

export class FieldSizeX extends Component
{
    render() {
        return <div>
            <input min={min} max={max} type="number" name="fieldsizex" value={appstate.fieldSizeX} onChange={e => {
                let step = 1;
                if (appstate.fieldSizeX.value >= 1000) step = 1000;
                else if (appstate.fieldSizeX.value >= 100) step = 100;
                else if (appstate.fieldSizeX.value >= 20) step = 10;
                else step = 1;

                if (parseInt(e.currentTarget.value) > appstate.fieldSizeX.value)
                    appstate.fieldSizeX.value = Math.round(Math.min(max, appstate.fieldSizeX.value + step));
                else
                    appstate.fieldSizeX.value = Math.round(Math.max(min, appstate.fieldSizeX.value - step));
            }}/>
            <label for="fieldsizex">Width of the field (in meters)</label>
        </div>;
    }
}