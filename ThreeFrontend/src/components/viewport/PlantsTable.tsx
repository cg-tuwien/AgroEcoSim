import { Component, h, Fragment } from "preact";
import appstate from "../../appstate";

export class PlantsTable extends Component
{
    render() {
        return <>
            <p>Renderer: {appstate.renderer}</p>
            <ul>
                {appstate.plants.value.map(plant => (<li>
                    Volume: {plant.V} m³
                </li>))}
            </ul>
        </>;
    }
}