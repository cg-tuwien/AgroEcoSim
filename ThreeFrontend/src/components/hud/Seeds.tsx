import { Component, h } from "preact";
import appstate from "../../appstate";
//import "wired-elements"

export class Seeds extends Component
{
    render() {
        return <div>
            Seeds: {appstate.obstacles.value.length}
            <button name="seeds" onClick={() => {appstate.obstacles.value.push("123"); console.log(appstate.obstacles.value.length)} }>Add new seed</button>
            <ul>
                {appstate.seeds.value.map((x, i) => (<li>
                    <input min={0} max={appstate.fieldSizeX.value} type="number" name={`seedpx-${i}`} value={x.PX} />
                    <input max={0} min={-appstate.fieldSizeD.value} type="number" name={`seedpy-${i}`} value={x.PY} />
                    <input min={0} max={appstate.fieldSizeZ.value} type="number" name={`seedpz-${i}`} value={x.PZ} />
                    <button onClick={() => appstate.removeSeedAt(i)}>DEL</button>
                </li>))}
            </ul>
        </div>;
    }
}