import { Component, h, Fragment } from "preact";
import appstate from "../../appstate";
import { DecodePlantName } from "../../helpers/Plant";
import { Primitives } from "../../helpers/Primitives";

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
                {primitive.type == Primitives.Cylinder || primitive.type == Primitives.Box ? <li>Wood ratio: {primitive.stats[2]}</li> : <></>}
                {primitive.type == Primitives.Rectangle ? <li>Irradiance: {primitive.stats[2]}</li> : <></>}
                {primitive.type == Primitives.Rectangle || primitive.type == Primitives.Box ? <li>Resources availability: {primitive.stats[3]}</li>: <></>}
                {primitive.type == Primitives.Rectangle || primitive.type == Primitives.Box ? <li>Production efficiency: {primitive.stats[4]}</li> : <></>}
                {primitive.type == Primitives.Rectangle || primitive.type == Primitives.Box ? <li>Relative Resources: {primitive.stats[5]}</li>: <></>}
                {primitive.type == Primitives.Rectangle || primitive.type == Primitives.Box ? <li>Relative Production: {primitive.stats[6]}</li> : <></>}
            </ul>
        </>) : (<></>);
    }
}