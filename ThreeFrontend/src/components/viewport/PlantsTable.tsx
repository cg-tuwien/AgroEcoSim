import { Component, h } from "preact";
import appstate from "../../appstate";

export class PlantsTable extends Component
{
    render() {
        return <ul>
            {appstate.plants.value.map(plant => (<li>
                Volume: {plant.V} m³
            </li>))}
        </ul>;
    }
}