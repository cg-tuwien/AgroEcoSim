import { Component, h } from "preact";
import appstate from "../../appstate";
import { VisualMappingOptions } from "../../helpers/Plant";
//import "wired-elements"

export function VisualMapping() {
    const tooltip="natural (green/brown by wood factor) \
    water (relative amount of water wrt. storage available in the plant part) \
    energy (relative amount of enery wrt. storage available in the plant part) \
    resource (relative success in resource acquisition wrt. to other plant parts) \
    production (relative success in producing new resources wrt. to other plant parts)";
    return <div>

        <select title={tooltip} name={'visualmapping'} onChange={e => appstate.visualMapping.value = e.currentTarget.value as VisualMappingOptions}>
            {
                Object.values(VisualMappingOptions).map(x => (<option value={x} selected={appstate.visualMapping.value == x}>{x}</option>))
            }
        </select>
        <label for={'visualmapping'} title={tooltip}>visual mapping</label>
    </div>;
}