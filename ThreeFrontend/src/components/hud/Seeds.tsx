import { Component, h } from "preact";
import appstate from "../../appstate";

export class Seeds extends Component
{
    render() {
        return <div>
            <button name="seeds" onClick={() => appstate.pushRndSeed()}>Add new seed</button>
            <ul>
                {appstate.seeds.value.map((x, i) => (<li>
                    <input min={0} max={appstate.fieldSizeX.value} type="number" name={`seedpx-${i}`} value={x.PX} onChange={e => x.PX.value = e.currentTarget.value} />
                    <input max={0} min={-appstate.fieldSizeD.value} type="number" name={`seedpy-${i}`} value={x.PY} onChange={e => x.PY.value = e.currentTarget.value}  />
                    <input min={0} max={appstate.fieldSizeZ.value} type="number" name={`seedpz-${i}`} value={x.PZ} onChange={e => x.PZ.value = e.currentTarget.value}  />
                    <button onClick={() => appstate.removeSeedAt(i)}>DEL</button>
                </li>))}
            </ul>
        </div>;
    }
}