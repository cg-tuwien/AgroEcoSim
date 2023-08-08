import { Component, h, Fragment } from "preact";
import appstate from "../../appstate";
import { DecodePlantName } from "../../helpers/Plant";

export function PlantsTable()
{
    const pickName = appstate.plantPick.value;
    //appstate.history.length > appstate.playPointer.value ? appstate.history[appstate.playPointer.value].s

    if (pickName == "")
        return <>
            <ul>
                {appstate.plants.value.map(plant => (<li>
                    Volume: {plant.V} m³
                </li>))}
            </ul>
        </>;
    else
    {
        const index = DecodePlantName(pickName);
        const primitive = appstate.scene.value[index.entity][index.primitive];
        return primitive ? (<>
            <p>Plant part: {appstate.plantPick.value}</p>
            <ul style={{listStyleType: "none"}}>
                <li>Water ratio: {primitive.stats[0]}</li>
                <li>Energy ratio: {primitive.stats[1]}</li>
                {primitive.type == 2 ? <li>Wood ratio: {primitive.stats[2]}</li> : <></>}
                {primitive.type == 8 ? <li>Irradiance: {primitive.stats[2]}</li> : <></>}
                {primitive.type == 8 ? <li>Resources availability: {primitive.stats[3]}</li>: <></>}
                {primitive.type == 8 ? <li>Production efficiency: {primitive.stats[4]}</li> : <></>}
            </ul>
        </>) : (<></>);
    }
}