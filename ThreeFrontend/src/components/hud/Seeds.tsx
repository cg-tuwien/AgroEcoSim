import { Component, h, Fragment } from "preact";
import appstate from "../../appstate";
import { Seed } from "../../helpers/Seed";
import { SelectionState, backgroundColor, neutralColor } from "../../helpers/Selection";

const hoverColor = backgroundColor.clone().lerpHSL(neutralColor, 0.25).toArray().map(x => Math.round(255*x));
const hoverColorStr = `rgb(${hoverColor[0]}, ${hoverColor[1]}, ${hoverColor[2]})`;

function compareSeeds(x: Seed, y: Seed) {
    return x.fieldIndex.value - y.fieldIndex.value;
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
                </li>))}
            </ul>
        </>;
    }
}