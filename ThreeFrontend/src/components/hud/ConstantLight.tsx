import { Component, h } from "preact";
import appstate from "../../appstate";
//import "wired-elements"

export function ConstantLight()
{
    return <div>
        <input type="checkbox" name="constantlight" checked={appstate.constantLight} onChange={e => appstate.constantLight.value = e.currentTarget.checked }/>
        <label for="constantlight">Constant light</label>
    </div>;
}