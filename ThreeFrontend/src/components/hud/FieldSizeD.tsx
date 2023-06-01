import { Component, h } from "preact";
import appstate from "../../appstate";
//import "wired-elements"

const min = 0.1;
const max = 1000;

export class FieldSizeD extends Component
{
    render() {
        return <div>
            <input min={min} max={max} type="number" name="fieldSizeD" value={appstate.fieldSizeD} onChange={e => {
                let step = 1;
                if (appstate.fieldSizeD.value >= 100) step = 100;
                else if (appstate.fieldSizeD.value >= 20) step = 10;
                else if (appstate.fieldSizeD.value >= 2) step = 1;
                else step = 0.1;

                if (parseInt(e.currentTarget.value) > appstate.fieldSizeD.value)
                    appstate.fieldSizeD.value = Math.round(Math.min(max, appstate.fieldSizeD.value + step));
                else
                    appstate.fieldSizeD.value = Math.round(Math.max(min, appstate.fieldSizeD.value - step));
            }}/>
            <label for="fieldSizeD">Depth of the soil (in meters)</label>
        </div>;
    }
}