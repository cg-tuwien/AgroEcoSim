import { Component, h, Fragment } from "preact";
import { useMemo } from "preact/hooks";
import appstate from "../../appstate";
import { Seed } from "../../helpers/Seed";
import { SelectionState, backgroundColor, neutralColor } from "../../helpers/Selection";
import { effectiveStartOffsetHours, formatClock, PlantAgeUnit, plantAgeInHours, plantAgeInUnit } from "../../helpers/TimeUnits";
import { DEFAULT_TIME_ZONE, timeOfTimestep } from "../../helpers/Sun";

const hoverColor = backgroundColor.clone().lerpHSL(neutralColor, 0.25).toArray().map(x => Math.round(255*x));
const hoverColorStr = `rgb(${hoverColor[0]}, ${hoverColor[1]}, ${hoverColor[2]})`;

function compareSeeds(x: Seed, y: Seed) {
    return x.fieldIndex.value - y.fieldIndex.value;
}

function PlantAge(props: { seed: Seed, index: number }) {
    const seed = props.seed;
    const hours = seed.startOffsetHours.value;
    const unit = seed.ageUnit.value;
    const valid = Number.isFinite(hours);
    const effective = valid ? effectiveStartOffsetHours(hours, appstate.hoursPerTick.value) : 0;
    const initialDate = appstate.initialDate.value;
    const timeZone = appstate.siteTimeZone.value ?? DEFAULT_TIME_ZONE;
    // Hovering any seed redraws the list. Resolve the site's calendar only when its inputs change.
    const seedDate = useMemo(() => {
        if (!valid) return "";
        try {
            return formatClock(timeOfTimestep(initialDate, effective, 1, timeZone), timeZone);
        } catch { return "outside the supported calendar"; }
    }, [valid, effective, initialDate, timeZone]);
    return <div>
        <label for={`seedage-${props.index}`}>Age:</label>
        <input id={`seedage-${props.index}`} name={`seedage-${props.index}`} type="number" step="any"
            value={valid ? plantAgeInUnit(hours, unit) : ""} aria-invalid={!valid || undefined}
            aria-describedby={`seedage-help-${props.index}`}
            onInput={e => seed.startOffsetHours.value = plantAgeInHours(e.currentTarget.valueAsNumber, unit)} />
        <select aria-label={`Age unit for plant ${props.index}`} value={unit}
            onChange={e => seed.ageUnit.value = e.currentTarget.value as PlantAgeUnit}>
            <option value="days">Days</option>
            <option value="years">Years</option>
        </select>
        <small id={`seedage-help-${props.index}`} style={{ display: "block" }}>
            Negative: growing before start. Zero: seed at start. Positive: seed introduced later. A year is 365 days.
        </small>
        {!valid ? <small role="alert">Enter a finite Age.</small>
            : <small style={{ display: "block" }}>
                {effective !== hours ? `Effective: ${plantAgeInUnit(effective, unit)} ${unit} (${effective} h). ` : ""}
                Seed date: {seedDate}
            </small>}
    </div>;
}

export class Seeds extends Component
{
    color(state: SelectionState) {
        switch (state) {
            case "hover":
            case "selecthover": return hoverColorStr;
            default: return "inherit";
        }
    }

    render() {
        return <>
            <button name="seeds" onClick={() => appstate.pushRndSeed()}>Add new seed</button>
            <button name="clear-seeds" onClick={() => appstate.clearSeeds()}>Clear seeds</button>
            <br/>
            <input type="number" min={1} step={1} name={"seeds-per-field"} value={+appstate.seedsPerField.value.toFixed(0)} onChange={e => appstate.seedsPerField.value = parseInt(e.currentTarget.value)}/>
            <button name="seeds-count" onClick={() => appstate.pushRndSeed(appstate.seedsPerField.value)}>Many seeds</button>per tray
            <br/>
            <input type="number" min={0} step={0.01} name={"seeds-optimal-distance"} value={+appstate.seedsOptimalDistance.value.toFixed(3)} onChange={e => appstate.seedsOptimalDistance.value = parseFloat(e.currentTarget.value)}/>
            <button name="seeds-dist" onClick={() => appstate.pushSeedRaster(appstate.seedsOptimalDistance.value)}>Evenly distributed seeds</button>
            <ul class="scrollable-listing">
                {appstate.seeds.value.sort(compareSeeds).map((x: Seed, i: number) => (<li style={{backgroundColor: this.color(x.state.value)}} onMouseEnter={() => appstate.seeds.value[i].hover()} onMouseLeave={() => appstate.seeds.value[i].unhover()}>
                    <div>
                        <span>{i}.</span>
                        <label for={`seedpx-${i}`} >Species:</label>
                        <select name={`seedspec-${i}`} onChange={e => x.species.value = e.currentTarget.value}>
                            {appstate.species.value.map(s => <option value={s.name.value} selected={x.species.value == s.name.value}>{s.name.value}</option>)}
                        </select>
                        <button style={{float: "right"}} onClick={() => appstate.removeSeedAt(i)}>x</button>
                    </div>
                    <div>
                        <label for={`seedpx-${i}`}>x:</label>
                        <input min={0} max={appstate.fieldSizeX.value} step={0.1} type="number" name={`seedpx-${i}`} value={+x.px.value.toFixed(4)} onChange={e => x.px.value = parseFloat(e.currentTarget.value)} />
                        <label for={`seedpy-${i}`}>y:</label>
                        <input max={0} min={-appstate.fieldSizeD.value} step={0.1} type="number" name={`seedpy-${i}`} value={+x.py.value.toFixed(4)} onChange={e => x.py.value = parseFloat(e.currentTarget.value)}  />
                        <label for={`seedpz-${i}`}>z:</label>
                        <input min={0} max={appstate.fieldSizeZ.value} step={0.1} type="number" name={`seedpz-${i}`} value={+x.pz.value.toFixed(4)} onChange={e => x.pz.value = parseFloat(e.currentTarget.value)}  />
                        <label for={`seedfi-${i}`}>f:</label>
                        <input min={0} max={appstate.terrainList?.length} step={1} type="number" name={`seedfi-${i}`} value={+x.fieldIndex.value} onChange={e => x.fieldIndex.value = parseInt(e.currentTarget.value)}  />
                    </div>
                    <PlantAge seed={x} index={i} />
                </li>))}
            </ul>
        </>;
    }
}
