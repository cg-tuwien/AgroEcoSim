import { Component, h } from "preact";
import appstate from "../../appstate";
import { encodeTime } from "../../helpers/TimeUnits";
//import "wired-elements"
//import {FormField, Slider} from 'preact-material-components';
//import 'preact-material-components/FormField/style.css';
//import 'preact-material-components/Slider/style.css';

const lookup = [128, 256, 512, 1024, 2048, 4096, 8192];
export class SamplesPerPixel extends Component
{
    findIndex(v: number = appstate.samplesPerPixel.value) {
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
            <input min={lookup[0]} max={lookup[lookup.length - 1]} type="number" name="samplesperpixel" value={appstate.samplesPerPixel} onChange={e => {
                const targetValue = parseInt(e.currentTarget.value);
                let index = 0;
                if (Math.abs(targetValue - appstate.samplesPerPixel.value) > 1)
                {
                    index = this.findIndex(targetValue);
                    if (index < lookup.length - 1 && (lookup[index + 1] - targetValue < targetValue - lookup[index]))
                        index++;
                    appstate.samplesPerPixel.value = lookup[index]
                }
                else
                {
                    index = this.findIndex();
                    if (targetValue > lookup[index])
                    {
                        if (index + 1 < lookup.length)
                            appstate.samplesPerPixel.value = lookup[index + 1];
                    }
                    else
                    {
                        if (index > 0)
                            appstate.samplesPerPixel.value = lookup[index - 1];
                    }
                }
                e.currentTarget.value = appstate.samplesPerPixel.value.toString(); //necessary at repeated value postings
            }}/>
            <label for="samplesperpixel">Samples per Pixel</label>
        </div>;
    }
}