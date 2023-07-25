import { Component, h } from "preact";
import appstate from "../../appstate";
import { Seed } from "../../helpers/Seed";
import { SelectionState, backgroundColor, neutralColor } from "../../helpers/Selection";

const hoverColor = backgroundColor.clone().lerpHSL(neutralColor, 0.25).toArray().map(x => Math.round(255*x));
const hoverColorStr = `rgb(${hoverColor[0]}, ${hoverColor[1]}, ${hoverColor[2]})`;

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
        return <div>
            <button name="seeds" onClick={() => appstate.pushRndSeed()}>Add new seed</button>
            <ul>
                {appstate.seeds.value.map((x: Seed, i) => (<li style={{backgroundColor: this.color(x.state.value)}} onMouseEnter={() => appstate.seeds.value[i].hover()} onMouseLeave={() => appstate.seeds.value[i].unhover()}>
                    <div>
                        <label for={`seedpx-${i}`} >Species:</label>
                        <select name={`seedspec-${i}`} onChange={e => x.species.value = e.currentTarget.value}>
                            {appstate.species.value.map(s => <option value={s.name.value} selected={x.species.value == s.name.value}>{s.name.value}</option>)}
                        </select>
                        <button style={{float: "right"}} onClick={() => appstate.removeSeedAt(i)}>🗙</button>
                    </div>
                    <div>
                        <label for={`seedpx-${i}`} >x:</label>
                        <input min={0} max={appstate.fieldSizeX.value} step={0.1} type="number" name={`seedpx-${i}`} value={+x.px.value.toFixed(4)} onChange={e => x.px.value = parseFloat(e.currentTarget.value)} />
                        <label for={`seedpy-${i}`}>y:</label>
                        <input max={0} min={-appstate.fieldSizeD.value} step={0.1} type="number" name={`seedpy-${i}`} value={+x.py.value.toFixed(4)} onChange={e => x.py.value = parseFloat(e.currentTarget.value)}  />
                        <label for={`seedpz-${i}`}>z:</label>
                        <input min={0} max={appstate.fieldSizeZ.value} step={0.1} type="number" name={`seedpz-${i}`} value={+x.pz.value.toFixed(4)} onChange={e => x.pz.value = parseFloat(e.currentTarget.value)}  />
                    </div>
                </li>))}
            </ul>
        </div>;
    }
}