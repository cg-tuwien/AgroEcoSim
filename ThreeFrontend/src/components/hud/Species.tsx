import { h, Fragment } from "preact";
import appstate from "../../appstate";
import { Species } from "src/helpers/Species";
import { computed, signal, useSignal } from "@preact/signals";
import { SpeciesItem } from "./SpeciesItem";

export const conflictStyle: h.JSX.CSSProperties = {
    borderColor: "#ee2211",
    borderStyle: "solid"
};

export const selectedSpecies = signal('');

export function SpeciesList()
{
    return <div>
        <select onChange={e => selectedSpecies.value = e.target[(e.target as HTMLSelectElement).selectedIndex].title}>
            <option></option>
            {appstate.species.value.map(x => <option title={x.name}>{x.name}{x.aka.value?.length > 0 ? ` (${x.aka})` : ''}</option>)}
        </select>
        <button name="species" onClick={() => appstate.pushRndSpecies()}>Add new species</button>

        {selectedSpecies.value?.length > 0 ? <SpeciesItem></SpeciesItem> : <></>}
    </div>;
}