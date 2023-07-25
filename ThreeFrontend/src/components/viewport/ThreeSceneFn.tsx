import { useEffect, useRef } from "preact/compat";
import { h } from 'preact';
import * as THREE from "three";
import { OrbitControls } from "three/examples/jsm/controls/OrbitControls"
import WEBGL from "three/examples/jsm/capabilities/WebGL"
import { Index } from "src/helpers/Scene";
import { Primitive } from "src/helpers/Primitives";
import appstate, { PlayState } from "../../appstate";
import { batch, useSignalEffect } from "@preact/signals";
import { Obstacle } from "src/helpers/Obstacle";
import { Seed } from "src/helpers/Seed";
import { backgroundColor } from "../../helpers/Selection";
import { TransformControls } from 'three/examples/jsm/controls/TransformControls';
import { BaseRequestObject } from "src/helpers/BaseRequestObject";
import { CreateBudMesh, CreateLeafMesh, CreateStemMesh, EncodePlantName, SetupMesh, UpdateBudMesh, UpdateLeafMesh, UpdateStemMesh, VisualizeBudMesh, VisualizeLeafMesh, VisualizeStemMesh, doubleGreyMaterial } from "../../helpers/Plant";

enum Clicks { None, Down, Up, Double };

interface IInitData {
    tanFOV: number;
    windowHeight: number;
}

interface ISeedRef {
    type: "seed";
    seed: Seed;
}

interface ITerrainRef {
    type: "terrain";
}

interface IPlantRef {
    type: "plant";
    index: Index;
    customMaterial: boolean;
}

interface IObstacleRef {
    type: "obstacle";
    obstacle: Obstacle;
}

type DataRef = ISeedRef | ITerrainRef | IPlantRef | IObstacleRef;

// export const TerrainLayer = 0;
// export const SeedsLayer = 1;
// export const ObstaclesLayer = 2;
// export const PlantsLayer = 3;


// const materialHovered = new THREE.MeshBasicMaterial({
//     color: 'orange',
//     polygonOffset: true,
//     polygonOffsetFactor: -1,
//     wireframe: true,
//     wireframeLinewidth: 2,
//     transparent: true,
//     opacity: 0.8,
//     depthWrite: false,
//     depthTest: true
// });

export const scene = new THREE.Scene();
let renderer: THREE.WebGLRenderer;
let perspectiveCamera: THREE.PerspectiveCamera;
let cameraControls: OrbitControls;

export default function ThreeSceneFn () {
    let initialized = false;

    const renderOnce = () => {
        if (scene && perspectiveCamera && renderer) {
            renderer.render(scene, perspectiveCamera);
            // if (hoveredScene)
            //     this.renderer.render(hoveredScene, perspectiveCamera);
        }
    }

    const initCameras = (updateListeners: boolean) => {
        if (!perspectiveCamera)
        {
            perspectiveCamera = new THREE.PerspectiveCamera(50, window.innerWidth / window.innerHeight, 0.025, 5000);
            perspectiveCamera.position.set(0, 3, 5);
        }
        //perspectiveCamera.layers.enableAll();
        if (renderer) {
            if (!cameraControls) {
                cameraControls = new OrbitControls(perspectiveCamera, renderer.domElement);
                //this.controls.enableKeys = false;
                //this.controls.keys = { LEFT: "", RIGHT: "", BOTTOM: "", UP: "" };
                cameraControls.enableRotate = true;
                cameraControls.screenSpacePanning = true;
                cameraControls.update();

                cameraControls.addEventListener("change", renderOnce);
                if (updateListeners){
                    renderer.domElement.addEventListener('wheel', renderOnce);
                    renderer.domElement.addEventListener('mousemove', onMouseMove);
                    renderer.domElement.addEventListener('dblclick', onDblClick);
                    renderer.domElement.addEventListener('mousedown', onMouseDown);
                    renderer.domElement.addEventListener('mouseup', onMouseUp);
                }
            }

            if (!appstate.transformControls) {
                appstate.transformControls = new TransformControls(perspectiveCamera, renderer.domElement);
                appstate.transformControls.addEventListener('change', renderOnce);
                appstate.transformControls.addEventListener('dragging-changed', (event) => cameraControls.enabled = ! event.value);
            }
        }
        //initData = { tanFOV: Math.tan( ( ( Math.PI / 180 ) * perspectiveCamera.fov / 2 ) ), windowHeight: window.innerHeight };
    }

    const initScene = () => {
        if (!initialized)
        {
            scene.clear();
            scene.background = backgroundColor;
            // hemiLight.color.setHSL( 0.6, 1, 0.6 );
            // hemiLight.groundColor.setHSL( 0.2, 0.2, 0.2 );
            hemiLight.position.set( 0, 10000, 0 );
            scene.add( hemiLight );
            initialized = true;

            scene.add(appstate.objSeeds);
            appstate.seeds.value.forEach((s: Seed) => appstate.objSeeds.add(s.mesh));

            scene.add(appstate.objTerrain);
            scene.add(appstate.objObstacles);
            scene.add(appstate.objPlants);

            scene.add(appstate.transformControls);
        }
        else
        {
            appstate.objSeeds.clear();
            appstate.objTerrain.clear();
            appstate.objObstacles.clear();
            appstate.objPlants.clear();
        }
        //scene.add(new THREE.AxesHelper( 1 ))

        //const mesh = new THREE.Mesh(threeBoxPrimitive, new THREE.MeshBasicMaterial({color: new THREE.Color("#ff4411")}));
        // //mesh.translateY(10);
        // //const m = new THREE.Matrix4().scale(new THREE.Vector3(1, 1.4, 1));
        // //const m = new THREE.Matrix4().setPosition(new THREE.Vector3(0.5, 0.5, 0));
        // //mesh.applyMatrix4(m);
        // scene.add(mesh);

        buildTerrain();
        buildPlants();
        renderOnce();
    }

    const onMouseMove = (event: MouseEvent) => {
        interaction(event.clientX, event.clientY, Clicks.None);
    };

    const onMouseDown = (event: MouseEvent) => {
        interaction(event.clientX, event.clientY, Clicks.Down);
    };

    const onMouseUp = (event: MouseEvent) => {
        interaction(event.clientX, event.clientY, Clicks.Up);

    };

    const onDblClick = (event: MouseEvent) => {
        interaction(event.clientX, event.clientY, Clicks.Double);
    };

    const interaction = (x: number, y: number, clicks: Clicks) => {
        if (renderer?.domElement && perspectiveCamera) {
            const dom = renderer?.domElement;

            mouse.x = ((x -  dom.offsetLeft) / renderer?.domElement.clientWidth) * 2 - 1;
            mouse.y = 1 - ((y - dom.offsetTop) / renderer?.domElement.clientHeight) * 2;
            mouse.inside = true;
            raycastScene(clicks);
        }
    };

    const raycastScene = (clicks: Clicks) => {
        if (mouse.inside && scene && perspectiveCamera) {
            const mousePoint = new THREE.Vector3(mouse.x, mouse.y, 1); //The mouse point in homogenous coordinates (1 at the end)
            mousePoint.unproject(perspectiveCamera);
            const raycaster = new THREE.Raycaster(perspectiveCamera.position, mousePoint.sub(perspectiveCamera.position).normalize());
            //mousePoint now contains direction
            //raycaster.layers.set(SeedsLayer); //only pickable objects
            //raycaster.params.Line = { threshold: 0.04 }
            appstate.grabbed.value?.forEach((x: Seed) => x.move(raycaster));

            const intersections = raycaster.intersectObjects(appstate.objSeeds.children, true);
            intersections.push(...raycaster.intersectObjects(appstate.objObstacles.children, true));
            intersections.push(...raycaster.intersectObjects(appstate.objPlants.children, true));
            intersections.sort((a,b) => a.distance < b.distance ? -1 : (a.distance > b.distance ? 1 : 0));

            batch(() => {
                let seedPick: Seed = undefined;
                let obstaclePick: Obstacle = undefined;
                let plantPick: string = "";
                if (intersections.length > 0) {
                    const closest = intersections[0];
                    if (closest.object.userData)
                    {
                        const ref = closest.object.userData as DataRef;
                        switch (ref.type)
                        {
                            case "seed":
                                seedPick = ref.seed;
                                pickingLogic(clicks, seedPick, raycaster);
                            break;
                            case "obstacle":
                                obstaclePick = ref.obstacle;
                                pickingLogic(clicks, obstaclePick, raycaster);
                            break;
                            case "plant":
                                if (clicks == Clicks.None)
                                    plantPick = EncodePlantName(ref.index);
                            break;
                        }
                    }
                }

                appstate.clearSeedHovers(seedPick);
                appstate.clearObstacleHovers(obstaclePick);
                if (clicks == Clicks.Double)
                {
                    appstate.clearSeedSelects(seedPick);
                    appstate.clearObstacleSelects(obstaclePick);
                }
                if (clicks == Clicks.Up)
                {
                    appstate.clearSeedGrabs(seedPick);
                    appstate.clearObstacleGrabs(obstaclePick);
                }
                appstate.plantPick.value = plantPick;
            });
        }
    };

    const updateObject3D = (mesh: THREE.Mesh, primitive: Primitive, index: Index) => {
        switch(primitive.type)
        {
            case 4: mesh.matrix.fromArray([primitive.radius, 0, 0, primitive.center[0], 0, primitive.radius, 0, primitive.center[1], 0, 0, primitive.radius, primitive.center[2], 0, 0, 0, 1]).transpose(); break;
            default: mesh.matrix.fromArray([...primitive.affineTransform, 0, 0, 0, 1]).transpose(); break;
        }

        switch(primitive.type)
        {
            case 1: mesh.geometry = threeCirclePrimitive;
                    if (mesh.material !== doubleGreyMaterial)
                    {
                        const tmp = mesh.material;
                        mesh.material = doubleGreyMaterial;
                        if (Array.isArray(tmp))
                            tmp.forEach(m => m.dispose());
                        else
                            tmp.dispose();
                    }
                    mesh = SetupMesh(primitive, index, mesh); break; //disk / no organ just obstacles in v3
            case 2: UpdateStemMesh(mesh, primitive, index);
                    mesh.matrix
                        .scale(new THREE.Vector3(primitive.radius, primitive.length, primitive.radius))
                        .setPosition(new THREE.Vector3(primitive.affineTransform[3] + mesh.matrix.elements[4] * 0.5, primitive.affineTransform[7] + mesh.matrix.elements[5] * 0.5, primitive.affineTransform[11] + mesh.matrix.elements[6] * 0.5));
                    break; //cylinder / stem
            case 4: mesh = UpdateBudMesh(mesh, primitive, index); break; //sphere / bud
            case 8: mesh = UpdateLeafMesh(mesh, primitive, index); break; //plane / leafs
        }
    }

    const buildObject3D = (primitive: Primitive, index: Index) => {
        let matrix: THREE.Matrix4;
        switch(primitive.type)
        {
            case 4: matrix = new THREE.Matrix4().fromArray([primitive.radius, 0, 0, primitive.center[0], 0, primitive.radius, 0, primitive.center[1], 0, 0, primitive.radius, primitive.center[2], 0, 0, 0, 1]).transpose(); break;
            default: matrix = new THREE.Matrix4().fromArray([...primitive.affineTransform, 0, 0, 0, 1]).transpose(); break;
        }

        let mesh: THREE.Mesh;
        switch(primitive.type)
        {
            case 1: mesh = SetupMesh(primitive, index, new THREE.Mesh(threeCirclePrimitive, doubleGreyMaterial)); break; //disk / no organ just obstacles in v3
            case 2: mesh = CreateStemMesh(primitive, index);
                    matrix = matrix
                        .scale(new THREE.Vector3(primitive.radius, primitive.length, primitive.radius))
                        .setPosition(new THREE.Vector3(primitive.affineTransform[3] + matrix.elements[4] * 0.5, primitive.affineTransform[7] + matrix.elements[5] * 0.5, primitive.affineTransform[11] + matrix.elements[6] * 0.5));
                    break; //cylinder / stem
            case 4: mesh = CreateBudMesh(primitive, index); break; //sphere / bud
            case 8: mesh = CreateLeafMesh(primitive, index); break; //plane / leafs
        }

        mesh.applyMatrix4(matrix);
        mesh.matrixAutoUpdate = false;

        appstate.objPlants.add(mesh);
    }

    const buildTerrain = () => {
        appstate.objTerrain.clear();
        const w = appstate.fieldSizeX.value;
        const d = appstate.fieldSizeD.value;
        const l = appstate.fieldSizeZ.value;

        //const box = new THREE.Box3().setFromCenterAndSize(new THREE.Vector3(w * 0.5, -d*0.5, l * 0.5), new THREE.Vector3(w, d, l) );
        //const terrainMesh = new THREE.Box3Helper( box, new THREE.Color("#cc9900") );
        const terrainMesh = new THREE.Mesh(threeBoxPrimitive, new THREE.MeshLambertMaterial({ color: 0x593700 }));
        terrainMesh.scale.set(w, d, l);
        terrainMesh.position.set(w * 0.5, -d*1, l * 0.5);
        terrainMesh.userData = { type: "terrain" };
        //terrainMesh.layers.set(TerrainLayer);
        appstate.objTerrain.add(terrainMesh);
        renderOnce();
    }

    const buildPlants = () => {
        const sceneData = appstate.scene.value;
        let counter = 0;
        const plants = appstate.objPlants.children;
        const entities = plants.length;

        for(let i = 0; i < sceneData.length; ++i)
        {
            const entity = sceneData[i];
            for(let j = 0; j < entity.length; ++j)
                counter < entities ? updateObject3D(plants[counter++] as THREE.Mesh, entity[j], {entity: i, primitive: j}) : buildObject3D(entity[j], {entity: i, primitive: j});
        }

        //dispose the remaining ones
        if (counter < entities)
            for(let i = entities - 1; i >= counter; --i)
                plants.splice(counter, entities - counter).map((x: THREE.Mesh) => {
                    if (x.userData.type == "plant" && x.userData.customMaterial)
                    {
                        const material = x.material;
                        if (Array.isArray(material))
                            material.forEach(m => m.dispose());
                        else
                            material.dispose();
                    }
                });

        renderOnce();

        setTimeout(() => {
            if (appstate.previewRequestAfterSceneUpdate)
            {
                appstate.previewRequestAfterSceneUpdate = false;
                appstate.previewRequest.value = true;
            }
            appstate.playRequest.value = true;
        }, appstate.playing.peek() == PlayState.ForwardWaiting ? 0 : 20);
    }

    //divContainer = createRef<HTMLDivElement>();
    //parent: HTMLElement | null | undefined;
    //let width: number;
    //let height: number;
    // let onHover: (id: number) => void;
    // let onDblClick: (id: number) => void;
    // let hasSelection: () => boolean;
    const hemiLight = new THREE.HemisphereLight( 0xffffff, 0x707070, 3.175 );

    //let transformControls: TransformControls | undefined;

    let animationRequest: number;
    let mouse = { inside: false, x: 0, y: 0};

    window.addEventListener("resize", () => {
        //if (this.width !== this.props.width || this.height !== this.props.height) {
            if (renderer)
                renderer.setSize(window.innerWidth, window.innerHeight);

            if ((window.innerWidth > 0 && window.innerHeight > 0) || !perspectiveCamera) {
                if (perspectiveCamera) {
                    perspectiveCamera.aspect = window.innerWidth / window.innerHeight;
                    //if (this.initData) perspectiveCamera.fov = ( 360 / Math.PI ) * Math.atan( this.initData.tanFOV * ( window.innerHeight / this.initData.windowHeight ) );
                    perspectiveCamera.updateProjectionMatrix();
                    cameraControls?.update();
                }
                else
                    initCameras(false);
                renderOnce();
            }
            else
            {
                perspectiveCamera = undefined;
                cameraControls?.dispose();
                cameraControls = undefined;
            }

        return () =>
        {
            animationRequest && cancelAnimationFrame(animationRequest);
            animationRequest = undefined;
            //scene has/needs no dispose anymore
            scene.clear();
            cameraControls?.dispose();
            cameraControls = undefined;
        }
    });


    useSignalEffect(() => {
        const anyGrab = appstate.grabbed.value?.length > 0;
        if (cameraControls) cameraControls.enabled = !anyGrab;
    });

    useSignalEffect(() => {
        appstate.objTerrain.clear();
        buildTerrain();
    });

    useSignalEffect(() => {
        buildPlants();
    })

    useSignalEffect(() => {
        if (appstate.needsRender.value) {
            appstate.needsRender.value = false;
            renderOnce();
        }
    });

    const divRef = useRef(null);

    useEffect(() => {
        let newRenderer = false;
        if (!renderer && WEBGL.isWebGL2Available()){
            const canvas = document.createElement('canvas');
            const context = canvas.getContext('webgl2', { alpha: false });
            if (context)
            {
                renderer = new THREE.WebGLRenderer( { antialias: true, powerPreference: 'high-performance',  canvas: canvas, context: context as any } );
                newRenderer = true;
            }
        }

        if (!renderer) {
            if ( WEBGL.isWebGLAvailable())
            {
                const canvas = document.createElement('canvas');
                const context = canvas.getContext('webgl', { alpha: false });
                renderer = new THREE.WebGLRenderer( { antialias: true, powerPreference: 'high-performance',  canvas: canvas, context: context as any } );
                newRenderer = true;
            }
            else
                divRef.current.appendChild(WEBGL.getWebGLErrorMessage());
        }

        initCameras(newRenderer);

        if (renderer && divRef.current) {
            renderer.setSize(window.innerWidth, window.innerHeight);
            divRef.current.appendChild(renderer.domElement);

            //this.renderer.gammaFactor = 2.2;
            //this.renderer.outputEncoding = THREE.GammaEncoding;
            renderer.outputColorSpace = THREE.SRGBColorSpace;
            renderer.useLegacyLights = true;

            renderer.setPixelRatio( window.devicePixelRatio || 1 );
            //this.renderer.setClearColor(new THREE.Color(0x1c1c1c));
            //this.renderer.toneMapping = THREE.NoToneMapping;
            renderer.toneMapping = THREE.LinearToneMapping;
            //this.renderer.toneMapping = THREE.ACESFilmicToneMapping;
            renderer.autoClear = false;
            initScene();
        }

        return () => divRef.current.removeChild(renderer.domElement);
    });

    useSignalEffect(() => {
        appstate.visualMapping.valueOf(); //manual subscription necessary
        appstate.objPlants.traverse((x: THREE.Mesh) => {
            const data = x.userData as IPlantRef;
            const material = x.material as THREE.MeshStandardMaterial;
            if (data.customMaterial)
            {
                if (x.geometry == threePlanePrimitive) //leaf
                    VisualizeLeafMesh(material, data.index);
                else if (x.geometry == threeCylinderPrimitive) //stem
                    VisualizeStemMesh(material, data.index);
                else //bud
                    VisualizeBudMesh(material, data.index);
            }
        })
        renderOnce();
    })

    function pickingLogic(clicks: Clicks, targetObject: BaseRequestObject, raycaster: THREE.Raycaster) {
        if (clicks == Clicks.Double) {
            if (targetObject.state.value != "select")
                targetObject.selecthover();
        }
        else if (clicks == Clicks.Down) {
            if (targetObject.state.value == "selecthover")
                targetObject.grab(raycaster);
        }
        else if (clicks == Clicks.Up) {
            if (targetObject.state.value == "grab")
                targetObject.ungrab("selecthover");
        }
        else switch (targetObject.state.value) {
            case "none": targetObject.hover(); break;
            case "select": targetObject.selecthover(); break;
        }
    }

    return <div id="main3Dviewport" ref={divRef}></div>;
}

export const threeBoxPrimitive = new THREE.BoxGeometry().translate(0, 0.5, 0); //box
export const threeSpherePrimitive = new THREE.SphereGeometry(1, 16, 16); //sphere
export const threeCylinderPrimitive = new THREE.CylinderGeometry(1, 1, 1.0, 16); //cylinder
export const threePlanePrimitive = new THREE.PlaneGeometry(2, 2); //rect
export const threeCirclePrimitive = new THREE.CircleGeometry(0.5).rotateX(-Math.PI * 0.5); //disk

