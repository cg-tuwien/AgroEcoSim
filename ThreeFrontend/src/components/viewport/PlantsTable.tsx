import { Component, h, Fragment } from "preact";
import appstate from "../../appstate";
import { DecodePlantName } from "../../helpers/Plant";
import { Primitive, Primitives } from "../../helpers/Primitives";

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
        let primitive : Primitive = undefined;
        if (appstate.scene.value.length > index.entity)
        {
            const ent = appstate.scene.value[index.entity];
            if (ent.length > index.primitive)
                primitive = ent[index.primitive];
        }

        return primitive ? (<>
            <p>Plant part: {appstate.plantPick.value}</p>
            <ul style={{listStyleType: "none"}}>
                <li>Water ratio: {primitive.stats[0]}</li>
                <li>Energy ratio: {primitive.stats[1]}</li>
                <li>Auxins: {primitive.stats[2]}</li>
                <li>Cytokinins: {primitive.stats[3]}</li>
                {primitive.type == Primitives.Cylinder || primitive.type == Primitives.Box ? <li>Wood ratio: {primitive.stats[4]}</li> : <></>}
                {primitive.type == Primitives.Rectangle ? <li>Irradiance: {primitive.stats[4]}</li> : <></>}
                {primitive.type == Primitives.Rectangle || primitive.type == Primitives.Box || primitive.type == Primitives.Cylinder
                    ? <><li>Resources availability: {primitive.stats[5]}</li>
                        <li>Production efficiency: {primitive.stats[6]}</li>
                        <li>Relative Resources: {primitive.stats[7]}</li>
                        <li>Relative Production: {primitive.stats[8]}</li></>
                    : <></>}
            </ul>
        </>) : (<></>);
    }
}