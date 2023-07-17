import { Component, h } from "preact";
import appstate from "../../appstate";
import { encodeTime } from "../../helpers/TimeUnits";

const day = 24;
const month = 30 * day;
const year = 365 * day;
const decade = 10 * year + 48;

export function TotalHours() {
    return <div>
        <input min={1} type="number" name="totalhours" value={appstate.totalHours} onChange={e => {
            const targetValue = parseInt(e.currentTarget.value);
            if (Math.abs(targetValue - appstate.totalHours.value) > 1)
                appstate.totalHours.value = Math.max(1, targetValue);
            else
            {
                if (targetValue > appstate.totalHours.value){
                    let step = 1;
                    if (appstate.totalHours.value >= decade) step = decade;
                    else if (appstate.totalHours.value >= year) step = year;
                    else if (appstate.totalHours.value >= month) step = month;
                    else if (appstate.totalHours.value >= day) step = day;

                    appstate.totalHours.value += step;
                }
                else
                {
                    let step = 1;
                    if (appstate.totalHours.value >= decade * 2) step = decade;
                    else if (appstate.totalHours.value >= year * 2) step = year;
                    else if (appstate.totalHours.value >= month * 2) step = month;
                    else if (appstate.totalHours.value >= day * 2) step = day;

                    appstate.totalHours.value = Math.max(1, appstate.totalHours.value - step);
                }
            }
        }}/>
        <label for="totalhours">Total Hours to be simulated</label>
        <label for="totalhours" class="unit">({encodeTime(appstate.totalHours.value)})</label>
    </div>;
}