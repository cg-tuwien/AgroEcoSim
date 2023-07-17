import { Component, h } from "preact";
import appstate from "../../appstate";
//import "wired-elements"

export function ConstantLight()
{
    const tooltip = "Disables global-illumination to speed up the computation. Constant light is assumed instead. Dedicated for debug purposes.";
    return <div>
        <input type="checkbox" name="constantlight" title={tooltip} checked={appstate.constantLight} onChange={e => appstate.constantLight.value = e.currentTarget.checked }/>
        <label for="constantlight" title={tooltip}>Constant light</label>
    </div>;
}