import { Component, h } from "preact";
import appstate from "../../appstate";
import { VisualMappingOptions } from "../../helpers/Plant";
//import "wired-elements"

export function VisualMapping() {
    return <div>

        <select name={'visualmapping'} onChange={e => appstate.visualMapping.value = e.currentTarget.value as VisualMappingOptions}>
            {
                Object.values(VisualMappingOptions).map(x => (<option value={x} selected={appstate.visualMapping.value == x}>{x}</option>))
            }
        </select>
        <label for={'visualmapping'}>visual mapping</label>
    </div>;
}