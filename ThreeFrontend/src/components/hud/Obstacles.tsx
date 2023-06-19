import { Component, h, Fragment } from "preact";
import appstate from "../../appstate";
import { Obstacle, ObstacleType } from "src/helpers/Obstacle";

export class Obstacles extends Component
{
    mapItem(x: Obstacle, i: number) {
        const common = <>
            <label for={`obstacleuh-${i}`}>height:</label>
            <input min={0} max={10000} step={0.1} type="number" name={`obstacleuh-${i}`} value={+x.height.value.toFixed(4)} onChange={e => x.height.value = parseFloat(e.currentTarget.value)} />
            <label for={`obstacleut-${i}`}>thick:</label>
            <input min={0} max={Math.max(appstate.fieldSizeX.value, appstate.fieldSizeZ.value) * 2} step={0.1} type="number" name={`obstacleut-${i}`} value={+x.thickness.value.toFixed(4)} onChange={e => x.thickness.value = parseFloat(e.currentTarget.value)} />
            </>;
        switch(x.type.value)
        {
            case "wall": return <>
                <label for={`obstaclewl-${i}`}>length:</label>
                <input min={0} max={Math.max(appstate.fieldSizeX.value, appstate.fieldSizeZ.value) * 2} step={0.1} type="number" name={`obstaclewl-${i}`} value={+x.wallLength_UmbrellaRadius.value.toFixed(4)} onChange={e => x.wallLength_UmbrellaRadius.value = parseFloat(e.currentTarget.value)} />
                {common}
                {/*<br/><input min={-180} max={180} type="number" name={`obstacleay-${i}`} value={x.AngleY} onChange={e => x.AngleY.value = parseFloat(e.currentTarget.value)} />
                <input min={-180} max={180} type="number" name={`obstacleax-${i}`} value={x.AngleX} onChange={e => x.AngleX.value = parseFloat(e.currentTarget.value)} />*/}
                </>;
            case "umbrella": return <>
                <label for={`obstacleur-${i}`}>radius:</label>
                <input min={0} max={Math.max(appstate.fieldSizeX.value, appstate.fieldSizeZ.value) * 2} step={0.1}type="number" name={`obstacleur-${i}`} value={+x.wallLength_UmbrellaRadius.value.toFixed(4)} onChange={e => x.wallLength_UmbrellaRadius.value = parseFloat(e.currentTarget.value)} />
                {common}
                </>;
            default: return (<></>);
        }
    }

    wrapItem(x:Obstacle, i: number) {
        return (<>
                <label for={`obstaclept-${i}`}>type:</label>
                <select name={`obstaclet-${i}`} onChange={e => x.type.value = e.currentTarget.value as ObstacleType}>
                    <option value="wall" selected={x.type.value == "wall"}>wall</option>
                    <option value="umbrella" selected={x.type.value == "umbrella"}>umbrella</option>
                </select>
                <button onClick={() => appstate.removeObstacleAt(i)}>🗙</button>
                <br/>
                <label for={`obstaclepx-${i}`}>x:</label>
                <input min={-appstate.fieldSizeZ.value} max={appstate.fieldSizeX.value} step={0.1} type="number" name={`obstaclepx-${i}`} value={x.px} onChange={e => x.px.value = parseFloat(e.currentTarget.value)} />
                <label for={`obstaclepy-${i}`}>y:</label>
                <input max={2*appstate.fieldSizeD.value} min={-appstate.fieldSizeD.value} step={0.1} type="number" name={`obstaclepy-${i}`} value={x.py} onChange={e => x.py.value = parseFloat(e.currentTarget.value)}  />
                <label for={`obstaclepz-${i}`}>z:</label>
                <input min={-appstate.fieldSizeZ.value} max={appstate.fieldSizeZ.value} step={0.1} type="number" name={`obstaclepz-${i}`} value={x.pz} onChange={e => x.pz.value = parseFloat(e.currentTarget.value)}  />
                <br/>

                {this.mapItem(x, i)}
            </>);
    }

    render() {
        return <div>
            <button name="obstacles" onClick={() => appstate.pushRndObstacle()}>Add new obstacle</button>
            <ul> {appstate.obstacles.value.map((x, i) => <li>{this.wrapItem(x, i)}</li>)} </ul>
        </div>;
    }
}