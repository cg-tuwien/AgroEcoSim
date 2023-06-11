import { Component, h, Fragment } from "preact";
import appstate, { IObstacleRequest } from "../../appstate";

export class Obstacles extends Component
{
    mapItem(x:IObstacleRequest, i: number) {
        switch(x.Type)
        {
            case "wall": return <>
                <input min={0} max={Math.max(appstate.fieldSizeX.value, appstate.fieldSizeZ.value) * 2} type="number" name={`obstaclewl-${i}`} value={x.Length} onChange={e => x.Length.value = parseFloat(e.currentTarget.value)} />
                <input min={0} max={10000} type="number" name={`obstaclewh-${i}`} value={x.Height} onChange={e => x.Height.value = parseFloat(e.currentTarget.value)} />
                <input min={0} max={Math.max(appstate.fieldSizeX.value, appstate.fieldSizeZ.value) * 2} type="number" name={`obstaclewt-${i}`} value={x.Thickness} onChange={e => x.Thickness.value = parseFloat(e.currentTarget.value)} />
                <input min={-180} max={180} type="number" name={`obstacleay-${i}`} value={x.AngleY} onChange={e => x.AngleY.value = parseFloat(e.currentTarget.value)} />
                <input min={-180} max={180} type="number" name={`obstacleax-${i}`} value={x.AngleX} onChange={e => x.AngleX.value = parseFloat(e.currentTarget.value)} />
                </>;
            case "umbrella": return <>
                <input min={0} max={Math.max(appstate.fieldSizeX.value, appstate.fieldSizeZ.value) * 2} type="number" name={`obstacleur-${i}`} value={x.DiskRadius} onChange={e => x.DiskRadius.value = parseFloat(e.currentTarget.value)} />
                <input min={0} max={10000} type="number" name={`obstacleuh-${i}`} value={x.Height} onChange={e => x.Height.value = parseFloat(e.currentTarget.value)} />
                <input min={0} max={Math.max(appstate.fieldSizeX.value, appstate.fieldSizeZ.value) * 2} type="number" name={`obstacleut-${i}`} value={x.PoleThickness} onChange={e => x.PoleThickness.value = parseFloat(e.currentTarget.value)} />
                </>;
            default: return (<></>);
        }
    }

    wrapItem(x:IObstacleRequest, i: number) {
        return (<>
                <input min={0} max={appstate.fieldSizeX.value} type="number" name={`obstaclepx-${i}`} value={x.PX} onChange={e => x.PX.value = parseFloat(e.currentTarget.value)} />
                <input max={0} min={-appstate.fieldSizeD.value} type="number" name={`obstaclepy-${i}`} value={x.PY} onChange={e => x.PY.value = parseFloat(e.currentTarget.value)}  />
                <input min={0} max={appstate.fieldSizeZ.value} type="number" name={`obstaclepz-${i}`} value={x.PZ} onChange={e => x.PZ.value = parseFloat(e.currentTarget.value)}  />
                <select name={`obstaclet-${i}`}>
                    <option value="wall" selected={x.Type == "wall"}>wall</option>
                    <option value="umbrella" selected={x.Type == "umbrella"}>umbrella</option>
                </select>
                {this.mapItem(x, i)}
                <button onClick={() => appstate.removeObstacle(i)}>DEL</button>
            </>);
    }

    render() {
        return <div>
            <button name="obstacles" onClick={() => appstate.pushRndObstacle()}>Add new obstacle</button>
            <ul> {appstate.obstacles.value.map((x, i) => <li>{this.wrapItem(x, i)}</li>)} </ul>
        </div>;
    }
}