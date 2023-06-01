import { Component, h } from "preact";
import appstate from "../../appstate";

const day = 24;
const month = 31 * day;
const year = 365 * day;
const decade = 10 * year + 48;

export class PlantsTable extends Component
{
    render() {
        return <ul>
            {appstate.platns.value.map(plant => (<li>
                Volume: {plant.V} m³
            </li>))}
        </ul>;
    }
}