import { Component, h, Fragment } from "preact";
import appstate from "../../appstate";
import { DecodePlantName } from "../../helpers/Plant";
import { Primitive, Primitives } from "../../helpers/Primitives";
import * as THREE from "three";
import { Seed } from "src/helpers/Seed";

const fixedPrecision = 4;

export function PickedDetails()
{
    const seedPick = appstate.seedPick.value;
    if (seedPick >= 0 && seedPick <= appstate.seeds.value.length)
    {
        const seed = appstate.seeds.value[seedPick];
        const v = new THREE.Vector3();
        seed.mesh.getWorldPosition(v);
        return <ul style={{listStyleType: "none"}}>
            <li>Index: {seedPick}</li>
            <li>Species: {seed.species.value}</li>
            <li>Position: {v.x.toFixed(fixedPrecision)}, {v.y.toFixed(fixedPrecision)}, {v.z.toFixed(fixedPrecision)}</li>
        </ul>;
    }

    const pickName = appstate.plantPick.value;
    //appstate.history.length > appstate.playPointer.value ? appstate.history[appstate.playPointer.value].s
    // return <>
    //         <ul>
    //             {appstate.plants.value.map(plant => (<li>
    //                 Volume: {plant.V} m³
    //             </li>))}
    //         </ul>
    //     </>;
    // else

    if (pickName?.length > 0)
    {
        const index = DecodePlantName(pickName);
        let primitive : Primitive = undefined;
        let affineTransform : Float32Array = undefined;
        let seed: Seed = undefined;
        if (appstate.scene.value.length > index.entity)
        {
            const ent = appstate.scene.value[index.entity];
            if (ent.primitives.length > index.primitive)
                primitive = ent.primitives[index.primitive];

            if (primitive.type != Primitives.Sphere)
                affineTransform = primitive.affineTransform;

            seed = appstate.seeds.peek()[index.entity];
        }

        return primitive ? (<>
            <p>Plant part: {appstate.plantPick.value}</p>
            <p>Species: {seed.species.value}</p>
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
                {affineTransform !== undefined ? <li>Position: {affineTransform[3].toFixed(fixedPrecision)}, {affineTransform[7].toFixed(fixedPrecision)}, {affineTransform[11].toFixed(fixedPrecision)}</li> : <></>}
            </ul>
        </>) : (<></>);
    }

    const terrainPick = appstate.terrainPick.value;
    if (terrainPick >= 0 && terrainPick < appstate.terrainList.length)
    {
        const terrain = appstate.terrainList[terrainPick];
        return (<>
            <p>Terrain: {terrain.id}</p>
        </>);
    }

    return <></>;
}