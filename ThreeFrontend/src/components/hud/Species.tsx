import { h, Fragment } from "preact";
import appstate from "../../appstate";
import { Species } from "src/helpers/Species";
import { computed, useSignal } from "@preact/signals";

const conflictStyle: h.JSX.CSSProperties = {
    borderColor: "#ee2211",
    borderStyle: "solid"
};

export function SpeciesList()
{
    return <div>
        <button name="species" onClick={() => appstate.pushRndSpecies()}>Add new species</button>
        <ul>
            {appstate.species.value.map((x: Species, i) => <SpeciesItem species={x} index={i}/>)}
        </ul>
    </div>;
}

export function SpeciesItem(props: {species: Species, index: number})
{
    const nameConflict = useSignal(false);
    const links = computed(() => appstate.seeds.value.reduce((a, c) => a + (c.species.value == props.species.name.value ? 1 : 0), 0));
    return <li>
        <div>
            <input type="text" name={`name-${props.index}`} value={props.species.name.value} title={"Name of the species"} style={nameConflict.value ? conflictStyle : null} onChange={e => {
                const name = e.currentTarget.value;
                if (appstate.species.value.some((s, i) => i !== props.index && s.name.value == name))
                {
                    nameConflict.value = true;
                    e.currentTarget.value = props.species.name.value;
                    setTimeout(() => nameConflict.value = false, 2000);
                }
                else
                {
                    props.species.name.value = name;
                    nameConflict.value = false;
                }
            }} />
            <label for={`name-${props.index}`}>Name {(nameConflict.value ? <span style={{color: "#ee2211"}}>conflict!</span> : <></>)}</label>
            <button style={{float: "right"}} onClick={() => appstate.removeSpeciesAt(props.index)} disabled={links.value > 0 || appstate.species.value.length <= 1}>🗙</button>
            <span style={{float: "right", marginRight: "0.5em"}}>🔗 {links.value}</span>
        </div>
        <div>
            <input min={0.001} step={0.1} type="number" name={`height-${props.index}`} value={+props.species.height.value.toFixed(4)} onChange={e => props.species.height.value = parseFloat(e.currentTarget.value)}  />
            <label for={`trunkToWood-${props.index}`}>Plant height</label>
        </div>
        <hr/>
        <div>
            <input min={0} max={1} step={0.1} type="number" name={`monopodialFactor-${props.index}`} value={+props.species.monopodialFactor.value.toFixed(4)} onChange={e => props.species.monopodialFactor.value = parseFloat(e.currentTarget.value)}  />
            <label for={`monopodialFactor-${props.index}`}>Monopodial factor</label>
        </div>
        <div>
            <input min={1} step={1} type="number" name={`lateralsPerNode-${props.index}`} value={+props.species.lateralsPerNode.value} onChange={e => props.species.lateralsPerNode.value = parseInt(e.currentTarget.value)}  />
            <label for={`lateralsPerNode-${props.index}`}>Laterals per node</label>
        </div>
        <div>
            <input min={0} max={360} step={0.1} type="number" name={`lateralAngle-${props.index}`} value={+props.species.lateralAngleDeg.value.toFixed(4)} onChange={e => props.species.lateralAngleDeg.value = parseFloat(e.currentTarget.value)}  />
            <label for={`lateralAngle-${props.index}`}>Lateral angle (deg) increment per node</label>
        </div>
        <hr/>
        <div>
            <input min={0.001} step={0.01} type="number" name={`leafLength-${props.index}`} value={+props.species.leafLength.value.toFixed(4)} onChange={e => props.species.leafLength.value = parseFloat(e.currentTarget.value)}  />
            <label for={`leafLength-${props.index}`}>Leaf length</label>
        </div>
        <div>
            <input min={0.001} step={0.01} type="number" name={`leafRadius-${props.index}`} value={+props.species.leafRadius.value.toFixed(4)} onChange={e => props.species.leafRadius.value = parseFloat(e.currentTarget.value)}  />
            <label for={`leafRadius-${props.index}`}>Leaf radius</label>
        </div>
        <div>
            <input min={0.01} step={1} type="number" name={`leafGrowthTime-${props.index}`} value={+props.species.leafGrowthTime.value.toFixed(4)} onChange={e => props.species.leafGrowthTime.value = parseFloat(e.currentTarget.value)}  />
            <label for={`leafGrowthTime-${props.index}`}>Leaf growth time</label>
        </div>
        <div>
            <input min={0.001} step={0.01} type="number" name={`petioleLength-${props.index}`} value={+props.species.petioleLength.value.toFixed(4)} onChange={e => props.species.petioleLength.value = parseFloat(e.currentTarget.value)}  />
            <label for={`petioleLength-${props.index}`}>Petiole length</label>
        </div>
        <div>
            <input min={0.001} step={0.01} type="number" name={`petioleRadius-${props.index}`} value={+props.species.petioleRadius.value.toFixed(4)} onChange={e => props.species.petioleRadius.value = parseFloat(e.currentTarget.value)}  />
            <label for={`petioleRadius-${props.index}`}>Petiole radius</label>
        </div>
    </li>;
}