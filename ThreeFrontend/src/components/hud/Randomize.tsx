import { Component, h } from "preact";
import appstate from "../../appstate";
//import "wired-elements"

export class Randomize extends Component
{
    render() {
        return <div>
            <input min={1} type="checkbox" name="randomize" checked={appstate.randomize} onChange={e => appstate.randomize.value = e.currentTarget.checked }/>
            <label for="randomize">Randomized initialization</label>
        </div>;
    }
}