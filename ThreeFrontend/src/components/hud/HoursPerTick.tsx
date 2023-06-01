import { Component, h } from "preact";
import appstate from "../../appstate";
//import "wired-elements"
//import {FormField, Slider} from 'preact-material-components';
//import 'preact-material-components/FormField/style.css';
//import 'preact-material-components/Slider/style.css';

const lookup = [1, 2, 3, 4, 6, 8, 12, 24, 48, 72, 96, 120, 144, 168];
export class HoursPerTick extends Component
{
    findIndex(v: number = appstate.hoursPerTick.value) {
        let result = -1;
        for(let i = 1; i < lookup.length; ++i)
            if (lookup[i] > v)
            {
                result = i - 1;
                break;
            }

        if (result < 0)
            result = lookup.length - 1;

        return result;
    }

    render() {
        return <div>
            <input min={1} max={lookup[lookup.length - 1]} type="number" name="hourspertick" value={appstate.hoursPerTick} onChange={e => {
                const targetValue = parseInt(e.currentTarget.value);
                let index = 0;
                if (Math.abs(targetValue - appstate.hoursPerTick.value) > 1)
                {
                    //debugger;
                    index = this.findIndex(targetValue);
                    console.log("target:", targetValue);
                    console.log("index:", index);
                    if (index < lookup.length - 1 && (lookup[index + 1] - targetValue < targetValue - lookup[index]))
                        index++;
                    appstate.hoursPerTick.value = lookup[index]
                }
                else
                {
                    index = this.findIndex();
                    if (parseInt(e.currentTarget.value) > lookup[index])
                    {
                        if (index + 1 < lookup.length)
                            appstate.hoursPerTick.value = lookup[index + 1];
                    }
                    else
                    {
                        if (index > 0)
                            appstate.hoursPerTick.value = lookup[index - 1];
                    }
                }
                e.currentTarget.value = appstate.hoursPerTick.value.toString(); //necessary at repeated value postings
            }}/>
            <label for="hourspertick">Hours per Tick</label>
        </div>;
    }
}