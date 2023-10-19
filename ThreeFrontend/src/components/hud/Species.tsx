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

const dominanceFactorTooltip = "Reduces the growth of lateral branches. Multiplies with each recursion level.";
const auxinsProductionTooltip = "Each meristem node generates this amount of auxins (given in unspecified units).";
const auxinsReachTooltip = "Auxines propagate this far within the plant with a linear falloff.";
const maxLeafLevelTooltip = "Limits the level of branches that support petioles. Technically it coresponds to the maximum possible level of descendants.";

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
            <input min={0} step={0.1} type="number" name={`height-${props.index}`} value={+props.species.height.value.toFixed(4)} onChange={e => props.species.height.value = parseFloat(e.currentTarget.value)}  />
            <label for={`height-${props.index}`}>Plant height</label>
        </div>
        <div>
            <input min={0} step={0.01} type="number" name={`nodeDist-${props.index}`} value={+props.species.nodeDist.value.toFixed(4)} onChange={e => props.species.nodeDist.value = parseFloat(e.currentTarget.value)}  />
            <label for={`nodeDist-${props.index}`}>Nodes distance</label>
            <span>var:&nbsp;</span>
            <input min={0} step={0.01} type="number" name={`nodeDistVar-${props.index}`} value={+props.species.nodeDistVar.value.toFixed(4)} onChange={e => props.species.nodeDistVar.value = parseFloat(e.currentTarget.value)}  />
        </div>
        <hr/>
        <div>
            <input min={0} max={1} step={0.1} type="number" name={`monopodialFactor-${props.index}`} value={+props.species.monopodialFactor.value.toFixed(4)} onChange={e => props.species.monopodialFactor.value = parseFloat(e.currentTarget.value)}  />
            <label for={`monopodialFactor-${props.index}`}>Monopodial factor</label>
        </div>
        <div>
            <input min={0} max={2} step={0.1} type="number" name={`dominanceFactor-${props.index}`} title={dominanceFactorTooltip} value={+props.species.dominanceFactor.value.toFixed(4)} onChange={e => props.species.dominanceFactor.value = parseFloat(e.currentTarget.value)}  />
            <label for={`dominanceFactor-${props.index}`} title={dominanceFactorTooltip}>Dominance factor</label>
        </div>
        <div>
            <input min={0} step={1} type="number" name={`auxinsProduction-${props.index}`} title={auxinsProductionTooltip} value={+props.species.auxinsProduction.value.toFixed(4)} onChange={e => props.species.auxinsProduction.value = parseFloat(e.currentTarget.value)}  />
            <label for={`auxinsProduction-${props.index}`} title={auxinsProductionTooltip}>Auxins production</label>
        </div>
        <div>
            <input min={0} step={0.1} type="number" name={`auxinsReach-${props.index}`} title={auxinsReachTooltip} value={+props.species.auxinsReach.value.toFixed(4)} onChange={e => props.species.auxinsReach.value = parseFloat(e.currentTarget.value)}  />
            <label for={`auxinsReach-${props.index}`} title={auxinsReachTooltip}>Auxins reach</label>
        </div>
        <div>
            <input min={1} step={1} type="number" name={`lateralsPerNode-${props.index}`} value={+props.species.lateralsPerNode.value} onChange={e => props.species.lateralsPerNode.value = parseInt(e.currentTarget.value)}  />
            <label for={`lateralsPerNode-${props.index}`}>Laterals per node</label>
        </div>
        <div>
            <input min={0} max={360} step={0.1} type="number" name={`lateralRoll-${props.index}`} value={+props.species.lateralRollDeg.value.toFixed(4)} onChange={e => props.species.lateralRollDeg.value = parseFloat(e.currentTarget.value)}  />
            <label for={`lateralRoll-${props.index}`}>Lateral roll (deg) increment</label>
            <span>var:&nbsp;</span>
            <input min={0} step={0.1} type="number" name={`lateralRollVar-${props.index}`} value={+props.species.lateralRollDegVar.value.toFixed(4)} onChange={e => props.species.lateralRollDegVar.value = parseFloat(e.currentTarget.value)}  />
        </div>
        <div>
            <input min={0} max={180} step={0.1} type="number" name={`lateralPitch-${props.index}`} value={+props.species.lateralPitchDeg.value.toFixed(4)} onChange={e => props.species.lateralPitchDeg.value = parseFloat(e.currentTarget.value)}  />
            <label for={`lateralPitch-${props.index}`}>Lateral pitch (deg)</label>
            <span>var:&nbsp;</span>
            <input min={0} step={0.1} type="number" name={`lateralPitchVar-${props.index}`} value={+props.species.lateralPitchDegVar.value.toFixed(4)} onChange={e => props.species.lateralPitchDegVar.value = parseFloat(e.currentTarget.value)}  />
        </div>
        <div>
            <input min={0} max={1} step={0.01} type="number" name={`twigsBending-${props.index}`} value={+props.species.twigsBending.value.toFixed(4)} onChange={e => props.species.twigsBending.value = parseFloat(e.currentTarget.value)}  />
            <label for={`twigsBending-${props.index}`}>Twigs bending rate</label>
        </div>
        <div>
            <input min={0} max={2} step={0.1} type="number" name={`bendingByLevel-${props.index}`} value={+props.species.bendingByLevel.value.toFixed(4)} onChange={e => props.species.bendingByLevel.value = parseFloat(e.currentTarget.value)}  />
            <label for={`bendingByLevel-${props.index}`}>Bending factor by hierarchy level</label>
        </div>
        <div>
            <input min={0} max={1} step={0.01} type="number" name={`apexBending-${props.index}`} value={+props.species.apexBending.value.toFixed(4)} onChange={e => props.species.apexBending.value = parseFloat(e.currentTarget.value)}  />
            <label for={`apexBending-${props.index}`}>Apex bending rate</label>
        </div>
        <div>
            <input min={0} max={1} step={0.01} type="number" name={`shootsGravitaxy-${props.index}`} value={+props.species.shootsGravitaxy.value.toFixed(4)} onChange={e => props.species.shootsGravitaxy.value = parseFloat(e.currentTarget.value)}  />
            <label for={`shootsGravitaxy-${props.index}`}>Shoots gravitaxy</label>
        </div>
        <hr/>
        {/*<div>
            <input min={0} step={1} type="number" name={`leafLevel-${props.index}`} value={+props.species.leafLevel.value} onChange={e => props.species.leafLevel.value = parseInt(e.currentTarget.value)}  />
            <label for={`leafLevel-${props.index}`}>Max. leaf level</label>
        </div>*/}
        <div>
            <input min={0} step={0.001} type="number" name={`leafLength-${props.index}`} value={+props.species.leafLength.value.toFixed(4)} onChange={e => props.species.leafLength.value = parseFloat(e.currentTarget.value)}  />
            <label for={`leafLength-${props.index}`}>Leaf length</label>
            <span>var:&nbsp;</span>
            <input min={0} step={0.1} type="number" name={`leafLengthVar-${props.index}`} value={+props.species.leafLengthVar.value.toFixed(4)} onChange={e => props.species.leafLengthVar.value = parseFloat(e.currentTarget.value)}  />
        </div>
        <div>
            <input min={0} step={0.001} type="number" name={`leafRadius-${props.index}`} value={+props.species.leafRadius.value.toFixed(4)} onChange={e => props.species.leafRadius.value = parseFloat(e.currentTarget.value)}  />
            <label for={`leafRadius-${props.index}`}>Leaf radius</label>
            <span>var:&nbsp;</span>
            <input min={0} step={0.1} type="number" name={`leafRadiusVar-${props.index}`} value={+props.species.leafRadiusVar.value.toFixed(4)} onChange={e => props.species.leafRadiusVar.value = parseFloat(e.currentTarget.value)}  />
        </div>
        <div>
            <input min={1} step={1} type="number" name={`leafGrowthTime-${props.index}`} value={+props.species.leafGrowthTime.value.toFixed(4)} onChange={e => props.species.leafGrowthTime.value = parseFloat(e.currentTarget.value)}  />
            <label for={`leafGrowthTime-${props.index}`}>Leaf growth time</label>
            <span>var:&nbsp;</span>
            <input min={0} step={1} type="number" name={`leafGrowthTimeVar-${props.index}`} value={+props.species.leafGrowthTimeVar.value.toFixed(4)} onChange={e => props.species.leafGrowthTimeVar.value = parseFloat(e.currentTarget.value)}  />
        </div>
        <div>
            <input min={0} step={0.1} type="number" name={`leafPitch-${props.index}`} value={+props.species.leafPitchDeg.value.toFixed(4)} onChange={e => props.species.leafPitchDeg.value = parseFloat(e.currentTarget.value)}  />
            <label for={`leafPitch-${props.index}`}>Leaf pitch (deg)</label>
            <span>var:&nbsp;</span>
            <input min={0} step={0.1} type="number" name={`leafPitchVar-${props.index}`} value={+props.species.leafPitchDegVar.value.toFixed(4)} onChange={e => props.species.leafPitchDegVar.value = parseFloat(e.currentTarget.value)}  />
        </div>
        <div>
            <input min={0} step={0.01} type="number" name={`petioleLength-${props.index}`} value={+props.species.petioleLength.value.toFixed(4)} onChange={e => props.species.petioleLength.value = parseFloat(e.currentTarget.value)}  />
            <label for={`petioleLength-${props.index}`}>Petiole length</label>
            <span>var:&nbsp;</span>
            <input min={0} step={0.01} type="number" name={`petioleLength-${props.index}`} value={+props.species.petioleLengthVar.value.toFixed(4)} onChange={e => props.species.petioleLengthVar.value = parseFloat(e.currentTarget.value)}  />
        </div>
        <div>
            <input min={0} step={0.001} type="number" name={`petioleRadius-${props.index}`} value={+props.species.petioleRadius.value.toFixed(4)} onChange={e => props.species.petioleRadius.value = parseFloat(e.currentTarget.value)}  />
            <label for={`petioleRadius-${props.index}`}>Petiole radius</label>
            <span>var:&nbsp;</span>
            <input min={0} step={0.001} type="number" name={`petioleRadius-${props.index}`} value={+props.species.petioleRadiusVar.value.toFixed(4)} onChange={e => props.species.petioleRadiusVar.value = parseFloat(e.currentTarget.value)}  />
        </div>
        <br/>
        <div>
            <input min={1} max={1} step={0.01} type="number" name={`rootsDensity-${props.index}`} value={+props.species.rootsDensity.value.toFixed(1)} onChange={e => props.species.rootsDensity.value = parseFloat(e.currentTarget.value)}  />
            <label for={`rootsDensity-${props.index}`}>Roots density</label>
        </div>
        <div>
            <input min={0} max={1} step={0.01} type="number" name={`rootsGravitaxis-${props.index}`} value={+props.species.rootsGravitaxis.value.toFixed(1)} onChange={e => props.species.rootsGravitaxis.value = parseFloat(e.currentTarget.value)}  />
            <label for={`rootsGravitaxis-${props.index}`}>Roots gravitaxis</label>
        </div>
    </li>;
}