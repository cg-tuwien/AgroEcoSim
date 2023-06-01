import { Component, h } from "preact";
import appstate from "../../appstate";
//import "wired-elements"

const min = 0.001;
const max = 1000;

function round(n: number)
{
    return Math.round(n * 1000) / 1000;
}

export class FieldResolution extends Component
{
    render() {
        return <div>
            <input min={min} max={max} type="number" name="fieldresolution" value={appstate.fieldResolution} onChange={e => {
                if (parseFloat(e.currentTarget.value) > appstate.fieldResolution.value){
                    let step = 1;
                    if (appstate.fieldResolution.value >= 5) step = 1;
                    else if (appstate.fieldResolution.value >= 0.2) step = 0.1;
                    else step = 0.01;

                    appstate.fieldResolution.value = round(Math.min(max, appstate.fieldResolution.value + step));
                }
                else
                {
                    let step = 1;
                    if (appstate.fieldResolution.value > 5) step = 1;
                    else if (appstate.fieldResolution.value > 0.2) step = 0.1;
                    else step = 0.01;

                    appstate.fieldResolution.value = round(Math.max(min, appstate.fieldResolution.value - step));
                }
            }}/>
            <label for="fieldresolution">Voxel size for the field (in meters)</label>
        </div>;
    }
}