import { Component, h, Fragment } from "preact";
import appstate from "../../appstate";
import { Obstacle, ObstacleType } from "src/helpers/Obstacle";
import { BoxTerrainItem, ITerrainItem } from "src/helpers/Terrain";

export class TerrainsList extends Component
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
            case "mesh": return <>
            </>
            default: return (<></>);
        }
    }

    wrapDetail(x: ITerrainItem, index: number) {
        const plants = appstate.seeds.value.filter(seed => seed.fieldIndex.value == index);
        return <>
            <div>{x.id}</div>
            <ol>{plants.map(p => <li>
                <div>{p.species.value}</div>
            </li>)}</ol>
            <button>Add</button>
            <button onClick={() => appstate.clearSeeds(x.fieldIndex.peek())}>Clear</button>
        </>
    }

    wrapOverview(x:ITerrainItem, index: number, seeds: number) {
        return (<>
                {x.id}
                <br/>
                {seeds} plants
                {/* <button onClick={() => appstate.removeTerrainAt(i)}>x</button> */}
                {/* <br/>
                <label for={`obstaclepx-${i}`}>x:</label>
                <input min={-appstate.fieldSizeZ.value} max={appstate.fieldSizeX.value} step={0.1} type="number" name={`obstaclepx-${i}`} value={x.px} onChange={e => x.px.value = parseFloat(e.currentTarget.value)} />
                <label for={`obstaclepy-${i}`}>y:</label>
                <input max={2*appstate.fieldSizeD.value} min={-appstate.fieldSizeD.value} step={0.1} type="number" name={`obstaclepy-${i}`} value={x.py} onChange={e => x.py.value = parseFloat(e.currentTarget.value)}  />
                <label for={`obstaclepz-${i}`}>z:</label>
                <input min={-appstate.fieldSizeZ.value} max={appstate.fieldSizeZ.value} step={0.1} type="number" name={`obstaclepz-${i}`} value={x.pz} onChange={e => x.pz.value = parseFloat(e.currentTarget.value)}  />
                <br/>

                {this.mapItem(x, i)} */}
            </>);
    }

    render() {
        const tmp = appstate.terrainTimestamp.value;
        if (appstate.terrainList?.length > 0 && tmp > 0)
        {
            //precompute to avoid n² complexity
            const counts = new Uint32Array(appstate.terrainList.length);
            appstate.seeds.value.map(seed => ++counts[seed.fieldIndex.value]);
            let selected = -1;
            for(let i = 0; i < appstate.terrainList?.length && selected == -1; ++i)
                if (appstate.terrainList[i].isSelected())
                    selected = i;

            return <div>
                <div>{appstate.terrainList?.length} regions</div>
                <div>
                    <input type="text" id="batchTerrainsClear"></input>
                    <button onClick={() => appstate.batchTerrainsClear((document.getElementById("batchTerrainsClear") as HTMLInputElement).value)}>Batch clear</button>
                </div>
                {selected >= 0 ? <><hr/>{this.wrapDetail(appstate.terrainList[selected], selected)}</> : <></>}
                <hr/>
                <ul class="scrollable-listing"> {appstate.terrainList.map((x, i) => <li>{x.isSelected() ? this.wrapDetail(x, i) : this.wrapOverview(x, i, counts[i])}</li>)} </ul>
            </div>;
        }
        else
            return <></>;
    }
}

function primitiveObstacleSelector(i: number, x: Obstacle) {
    return <select name={`obstaclet-${i}`} onChange={e => x.type.value = e.currentTarget.value as ObstacleType}>
        <option value="wall" selected={x.type.value == "wall"}>wall</option>
        <option value="umbrella" selected={x.type.value == "umbrella"}>umbrella</option>
    </select>;
}

function meshObstacleSelector(i: number, x: Obstacle) {
    return <span>Obstacle</span>;
}
