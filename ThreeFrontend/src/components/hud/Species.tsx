import { h, Fragment } from "preact";
import appstate from "../../appstate";
import { Species } from "src/helpers/Species";
import { computed, signal, useSignal } from "@preact/signals";

const conflictStyle: h.JSX.CSSProperties = {
    borderColor: "#ee2211",
    borderStyle: "solid"
};

const selectedSpecies = signal('');

export function SpeciesList()
{
    return <div>
        <select onChange={e => selectedSpecies.value = e.target[(e.target as HTMLSelectElement).selectedIndex].title}>
            <option></option>
            {appstate.species.value.map(x => <option title={x.name}>{x.name}{x.aka.value?.length > 0 ? ` (${x.aka})` : ''}</option>)}
        </select>
        <button name="species" onClick={() => appstate.pushRndSpecies()}>Add new species</button>

        {/* <ul>
            {appstate.species.value.map((x: Species, i) => <SpeciesItem species={x} index={i}/>)}
        </ul> */}
        {selectedSpecies.value?.length > 0 ? <SpeciesItem></SpeciesItem> : <></>}
    </div>;
}

const dominanceFactorTooltip = "Reduces the growth of lateral branches. Multiplies with each recursion level.";
const auxinsProductionTooltip = "Each meristem node generates this amount of auxins (given in unspecified units).";
const auxinsReachTooltip = "Auxines propagate this far within the plant with a linear falloff.";
const maxLeafLevelTooltip = "Limits the level of branches that support petioles. Technically it coresponds to the maximum possible level of descendants.";

export function SpeciesItem()
{
    const inputList = appstate.species.value;
    const index = inputList.findIndex(x => x.name.value == selectedSpecies.value);
    if (index < 0) return <></>;
    const species = inputList[index];

    const nameConflict = useSignal(false);
    const links = computed(() => appstate.seeds.value.reduce((a, c) => a + (c.species.value == species.name.value ? 1 : 0), 0));
    return <div class="speciesDetails">
        <div>
            <input type="text" name={`name-${index}`} value={species.name.value} title={"Name of the species"} style={nameConflict.value ? conflictStyle : null} class="speciesNameInput" onChange={e => {
                const name = e.currentTarget.value;
                if (appstate.species.value.some((s, i) => i !== index && s.name.value == name))
                {
                    nameConflict.value = true;
                    e.currentTarget.value = species.name.value;
                    setTimeout(() => nameConflict.value = false, 2000);
                }
                else
                {
                    species.name.value = name;
                    nameConflict.value = false;
                }
            }} />
            <label for={`name-${index}`}>Name {(nameConflict.value ? <span style={{color: "#ee2211"}}>conflict!</span> : <></>)}</label>
            <button style={{float: "right"}} onClick={() => appstate.removeSpeciesAt(index)} disabled={links.value > 0 || appstate.species.value.length <= 1}>🗙</button>
            <span style={{float: "right", marginRight: "0.5em"}}>🔗 {links.value}</span>
            <label for={`aka-${index}`} title={"Colloquial name"}>aka</label>
            <input type="text" name={`aka-${index}`} value={species.aka.value ?? ''} title={"Colloquial name"} class="speciesAkaInput" onChange={e => species.aka.value = e.currentTarget.value} />
        </div>
        <div>
            <select name={`behavior-${index}`} onChange={e => species.behaviorIndex.value = (e.target as HTMLSelectElement).selectedIndex}>
                {appstate.behaviors.value.map((x, i) => <option selected={species.behaviorIndex.value == i}>{x}</option>)}
            </select>
            <label for={`behavior-${index}`}>Behavior</label>
        </div>
        <div>
            <input min={0} step={0.1} type="number" name={`height-${index}`} value={+species.height.value.toFixed(4)} onChange={e => species.height.value = parseFloat(e.currentTarget.value)}  />
            <label for={`height-${index}`}>Plant height</label>
        </div>
        <div>
            <input min={0} step={0.01} type="number" name={`nodeDist-${index}`} value={+species.nodeDistance.value.toFixed(4)} onChange={e => species.nodeDistance.value = parseFloat(e.currentTarget.value)}  />
            <label for={`nodeDist-${index}`}>Nodes distance</label>
            <span>var:&nbsp;</span>
            <input min={0} step={0.01} type="number" name={`nodeDistVar-${index}`} value={+species.nodeDistanceVar.value.toFixed(4)} onChange={e => species.nodeDistanceVar.value = parseFloat(e.currentTarget.value)}  />
        </div>
        <hr/>
        <div>
            <input min={0} max={1} step={0.1} type="number" name={`monopodialFactor-${index}`} value={+species.monopodialFactor.value.toFixed(4)} onChange={e => species.monopodialFactor.value = parseFloat(e.currentTarget.value)}  />
            <label for={`monopodialFactor-${index}`}>Monopodial factor</label>
        </div>
        <div>
            <input min={0} max={2} step={0.1} type="number" name={`dominanceFactor-${index}`} title={dominanceFactorTooltip} value={+species.dominanceFactor.value.toFixed(4)} onChange={e => species.dominanceFactor.value = parseFloat(e.currentTarget.value)}  />
            <label for={`dominanceFactor-${index}`} title={dominanceFactorTooltip}>Dominance factor</label>
        </div>
        <div>
            <input min={0} step={1} type="number" name={`auxinsProduction-${index}`} title={auxinsProductionTooltip} value={+species.auxinsProduction.value.toFixed(4)} onChange={e => species.auxinsProduction.value = parseFloat(e.currentTarget.value)}  />
            <label for={`auxinsProduction-${index}`} title={auxinsProductionTooltip}>Auxins production</label>
        </div>
        <div>
            <input min={0} step={0.1} type="number" name={`auxinsReach-${index}`} title={auxinsReachTooltip} value={+species.auxinsReach.value.toFixed(4)} onChange={e => species.auxinsReach.value = parseFloat(e.currentTarget.value)}  />
            <label for={`auxinsReach-${index}`} title={auxinsReachTooltip}>Auxins reach</label>
        </div>
        <div>
            <input min={1} step={1} type="number" name={`lateralsPerNode-${index}`} value={+species.lateralsPerNode.value} onChange={e => species.lateralsPerNode.value = parseInt(e.currentTarget.value)}  />
            <label for={`lateralsPerNode-${index}`}>Laterals per node</label>
        </div>
        <div>
            <input min={0} max={360} step={0.1} type="number" name={`lateralRoll-${index}`} value={+species.lateralRollDeg.value.toFixed(4)} onChange={e => species.lateralRollDeg.value = parseFloat(e.currentTarget.value)}  />
            <label for={`lateralRoll-${index}`}>Lateral roll (deg) increment</label>
            <span>var:&nbsp;</span>
            <input min={0} step={0.1} type="number" name={`lateralRollVar-${index}`} value={+species.lateralRollDegVar.value.toFixed(4)} onChange={e => species.lateralRollDegVar.value = parseFloat(e.currentTarget.value)}  />
        </div>
        <div>
            <input min={0} max={180} step={0.1} type="number" name={`lateralPitch-${index}`} value={+species.lateralPitchDeg.value.toFixed(4)} onChange={e => species.lateralPitchDeg.value = parseFloat(e.currentTarget.value)}  />
            <label for={`lateralPitch-${index}`}>Lateral pitch (deg)</label>
            <span>var:&nbsp;</span>
            <input min={0} step={0.1} type="number" name={`lateralPitchVar-${index}`} value={+species.lateralPitchDegVar.value.toFixed(4)} onChange={e => species.lateralPitchDegVar.value = parseFloat(e.currentTarget.value)}  />
        </div>
        <div>
            <input min={0} max={1} step={0.01} type="number" name={`twigsBending-${index}`} value={+species.twigsBending.value.toFixed(4)} onChange={e => species.twigsBending.value = parseFloat(e.currentTarget.value)}  />
            <label for={`twigsBending-${index}`}>Twigs bending rate</label>
        </div>
        <div>
            <input min={0} max={2} step={0.1} type="number" name={`bendingByLevel-${index}`} value={+species.bendingByLevel.value.toFixed(4)} onChange={e => species.bendingByLevel.value = parseFloat(e.currentTarget.value)}  />
            <label for={`bendingByLevel-${index}`}>Bending factor by hierarchy level</label>
        </div>
        <div>
            <input min={0} max={1} step={0.01} type="number" name={`apexBending-${index}`} value={+species.twigsBendingApical.value.toFixed(4)} onChange={e => species.twigsBendingApical.value = parseFloat(e.currentTarget.value)}  />
            <label for={`apexBending-${index}`}>Apex bending rate</label>
        </div>
        <div>
            <input min={0} max={1} step={0.01} type="number" name={`shootsGravitaxis-${index}`} value={+species.shootsGravitaxis.value.toFixed(4)} onChange={e => species.shootsGravitaxis.value = parseFloat(e.currentTarget.value)}  />
            <label for={`shootsGravitaxis-${index}`}>Shoots gravitaxis</label>
        </div>
        <div>
            <input min={1} step={1} type="number" name={`woodGrowthTime-${index}`} value={+species.woodGrowthTime.value.toFixed(4)} onChange={e => species.woodGrowthTime.value = parseFloat(e.currentTarget.value)}  />
            <label for={`woodGrowthTime-${index}`}>Wood growth time (days)</label>
            <span>var:&nbsp;</span>
            <input min={0} step={1} type="number" name={`woodGrowthTimeVar-${index}`} value={+species.woodGrowthTimeVar.value.toFixed(4)} onChange={e => species.woodGrowthTimeVar.value = parseFloat(e.currentTarget.value)}  />
        </div>
        <hr/>
        {/*<div>
            <input min={0} step={1} type="number" name={`leafLevel-${index}`} value={+species.leafLevel.value} onChange={e => species.leafLevel.value = parseInt(e.currentTarget.value)}  />
            <label for={`leafLevel-${index}`}>Max. leaf level</label>
        </div>*/}
        <div>
            <input min={0} step={0.001} type="number" name={`leafLength-${index}`} value={+species.leafLength.value.toFixed(4)} onChange={e => species.leafLength.value = parseFloat(e.currentTarget.value)}  />
            <label for={`leafLength-${index}`}>Leaf length</label>
            <span>var:&nbsp;</span>
            <input min={0} step={0.1} type="number" name={`leafLengthVar-${index}`} value={+species.leafLengthVar.value.toFixed(4)} onChange={e => species.leafLengthVar.value = parseFloat(e.currentTarget.value)}  />
        </div>
        <div>
            <input min={0} step={0.001} type="number" name={`leafRadius-${index}`} value={+species.leafRadius.value.toFixed(4)} onChange={e => species.leafRadius.value = parseFloat(e.currentTarget.value)}  />
            <label for={`leafRadius-${index}`}>Leaf radius</label>
            <span>var:&nbsp;</span>
            <input min={0} step={0.1} type="number" name={`leafRadiusVar-${index}`} value={+species.leafRadiusVar.value.toFixed(4)} onChange={e => species.leafRadiusVar.value = parseFloat(e.currentTarget.value)}  />
        </div>
        <div>
            <input min={1} step={1} type="number" name={`leafGrowthTime-${index}`} value={+species.leafGrowthTime.value.toFixed(4)} onChange={e => species.leafGrowthTime.value = parseFloat(e.currentTarget.value)}  />
            <label for={`leafGrowthTime-${index}`}>Leaf growth time</label>
            <span>var:&nbsp;</span>
            <input min={0} step={1} type="number" name={`leafGrowthTimeVar-${index}`} value={+species.leafGrowthTimeVar.value.toFixed(4)} onChange={e => species.leafGrowthTimeVar.value = parseFloat(e.currentTarget.value)}  />
        </div>
        <div>
            <input min={0} step={0.1} type="number" name={`leafPitch-${index}`} value={+species.leafPitchDeg.value.toFixed(4)} onChange={e => species.leafPitchDeg.value = parseFloat(e.currentTarget.value)}  />
            <label for={`leafPitch-${index}`}>Leaf pitch (deg)</label>
            <span>var:&nbsp;</span>
            <input min={0} step={0.1} type="number" name={`leafPitchVar-${index}`} value={+species.leafPitchDegVar.value.toFixed(4)} onChange={e => species.leafPitchDegVar.value = parseFloat(e.currentTarget.value)}  />
        </div>
        <div>
            <input min={0} step={0.01} type="number" name={`petioleLength-${index}`} value={+species.petioleLength.value.toFixed(4)} onChange={e => species.petioleLength.value = parseFloat(e.currentTarget.value)}  />
            <label for={`petioleLength-${index}`}>Petiole length</label>
            <span>var:&nbsp;</span>
            <input min={0} step={0.01} type="number" name={`petioleLength-${index}`} value={+species.petioleLengthVar.value.toFixed(4)} onChange={e => species.petioleLengthVar.value = parseFloat(e.currentTarget.value)}  />
        </div>
        <div>
            <input min={0} step={0.001} type="number" name={`petioleRadius-${index}`} value={+species.petioleRadius.value.toFixed(4)} onChange={e => species.petioleRadius.value = parseFloat(e.currentTarget.value)}  />
            <label for={`petioleRadius-${index}`}>Petiole radius</label>
            <span>var:&nbsp;</span>
            <input min={0} step={0.001} type="number" name={`petioleRadius-${index}`} value={+species.petioleRadiusVar.value.toFixed(4)} onChange={e => species.petioleRadiusVar.value = parseFloat(e.currentTarget.value)}  />
        </div>
        <br/>
        <div>
            <input min={1} max={1} step={0.01} type="number" name={`rootsDensity-${index}`} value={+species.rootsDensity.value.toFixed(1)} onChange={e => species.rootsDensity.value = parseFloat(e.currentTarget.value)}  />
            <label for={`rootsDensity-${index}`}>Roots density</label>
        </div>
        <div>
            <input min={0} max={1} step={0.01} type="number" name={`rootsGravitaxis-${index}`} value={+species.rootsGravitaxis.value.toFixed(1)} onChange={e => species.rootsGravitaxis.value = parseFloat(e.currentTarget.value)}  />
            <label for={`rootsGravitaxis-${index}`}>Roots gravitaxis</label>
        </div>
        <hr/>
        <div>
            <input type="checkbox" name={`includeInRndGen-${index}`} checked={species.includeInRndGen} onChange={e => species.includeInRndGen.value = e.currentTarget.checked} />
            <label for={`includeInRndGen-${index}`}>Include in random seeding</label>
        </div>
    </div>;
}