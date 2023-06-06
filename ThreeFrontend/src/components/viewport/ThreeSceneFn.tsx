import { createRef, useEffect, useLayoutEffect, useRef } from "preact/compat";
import { Component, h, render } from 'preact';
import * as THREE from "three";
import { OrbitControls } from "three/examples/jsm/controls/OrbitControls"
import { FlyControls } from "three/examples/jsm/controls/FlyControls"
import WEBGL from "three/examples/jsm/capabilities/WebGL"
import { Index, Scene } from "src/helpers/Scene";
import { Primitive } from "src/helpers/Primitives";
import appstate from "../../appstate";
import { effect } from "@preact/signals";

interface IProps {
//     width: number,
//     height: number,
//     onHover(id: number): void,
//     onDblClick(id: number): void,
//     hasSelection(): boolean,
    //parent: HTMLElement;
};

interface IInitData {
    tanFOV: number;
    windowHeight: number;
}

const materialHovered = new THREE.MeshBasicMaterial({
    color: 'orange',
    polygonOffset: true,
    polygonOffsetFactor: -1,
    wireframe: true,
    wireframeLinewidth: 2,
    transparent: true,
    opacity: 0.8,
    depthWrite: false,
    depthTest: true
});

export default function ThreeSceneFn () {
    const renderOnce = () => {
        if (scene && perspectiveCamera && renderer) {
            renderer.render(scene, perspectiveCamera);
            console.log("render THREE");
            // if (hoveredScene)
            //     this.renderer.render(hoveredScene, perspectiveCamera);
        }
    }

    const initCameras = () => {
        perspectiveCamera = new THREE.PerspectiveCamera(50, window.innerWidth / window.innerHeight, 0.025, 5000);
        perspectiveCamera.position.set(0, 20, 30);
        if (renderer){
            console.log(renderer.domElement)
            controls = new OrbitControls(perspectiveCamera, renderer.domElement);
            //this.controls.enableKeys = false;
            //this.controls.keys = { LEFT: "", RIGHT: "", BOTTOM: "", UP: "" };
            controls.enableRotate = true;
            controls.screenSpacePanning = true;

            //this.controls.addEventListener("start", () => { this.performRendering = true });
            //this.controls.addEventListener("end", () => { this.performRendering = false });
            controls.addEventListener("change", () => renderOnce());
            renderer?.domElement.addEventListener('wheel', () => renderOnce());
        }

        //initData = { tanFOV: Math.tan( ( ( Math.PI / 180 ) * perspectiveCamera.fov / 2 ) ), windowHeight: window.innerHeight };
    }

    const initScene = () => {
        scene.clear();
        scene.background = new THREE.Color(getComputedStyle(document.documentElement).getPropertyValue("background"));

        // hemiLight.color.setHSL( 0.6, 1, 0.6 );
        // hemiLight.groundColor.setHSL( 0.2, 0.2, 0.2 );
        hemiLight.position.set( 0, 10000, 0 );
        scene.add( hemiLight );

        //const mesh = new THREE.Mesh(threeCylinderPrimitive, new THREE.MeshBasicMaterial({color: new THREE.Color("#ff4411")}));
        //mesh.translateY(10);
        //const m = new THREE.Matrix4().scale(new THREE.Vector3(1, 1.4, 1));
        //mesh.applyMatrix4(m);
        //scene.add(mesh);
        renderOnce();
    }

    const onMouseMove = (event: MouseEvent) => {
        this.interaction(event.clientX, event.clientY, false);
    };

    const onDblClick = (event: MouseEvent) => {
        this.interaction(event.clientX, event.clientY, true);
    };

    const interaction = (x: number, y: number, isDblClick: boolean) => {
        if (this.divContainer.current && perspectiveCamera) {
            const offsetLeft = this.divContainer.current.offsetLeft;
            const offsetTop = this.divContainer.current.offsetTop;

            if (x < offsetLeft || y < offsetTop ||
                x > offsetLeft + this.props.width || y > offsetTop + this.props.height)
                mouse.inside = false;
            else {
                mouse.x = ((x - offsetLeft) / this.props.width) * 2 - 1;
                mouse.y = 1 - ((y - offsetTop) / this.props.height) * 2;
                mouse.inside = true;

                if (this.props.hasSelection())
                {
                    if (isDblClick)
                    {
                        this.props.onDblClick(-1);
                        this.raycastScene(false);
                    }
                }
                else
                    this.raycastScene(isDblClick);
            }
        }
    };

    const raycastScene = (isDblClick: boolean) => {
        if (mouse.inside && scene && perspectiveCamera) {
            const mousePoint = new THREE.Vector3(mouse.x, mouse.y, 1); //The mouse point in homogenous coordinates (1 at the end)
            mousePoint.unproject(perspectiveCamera);
            const raycaster = new THREE.Raycaster(perspectiveCamera.position, mousePoint.sub(perspectiveCamera.position).normalize());
            raycaster.layers.enable(1); //only Michelangelo's objects
            //raycaster.params.Line = { threshold: 0.04 }

            const intersections = raycaster.intersectObjects(scene.children, false);

            // if (intersections.length > 0) {
            //     const closest = intersections[0];
            //     if (((hovered?.object.name != closest.object.name) || isDblClick))
            //     {
            //         //scene has/needs no dispose anymore, thus no need for hoveredScene.dispose();
            //         hoveredScene = new THREE.Scene();
            //         hoveredScene.overrideMaterial = this.materialHovered;

            //         hovered = closest;
            //         if (hovered?.object)
            //         {
            //             const n = parseInt(hovered?.object.name);
            //             this.props.onHover(n);
            //             if (isDblClick)
            //                 this.props.onDblClick(n)

            //             const hoveredClone = hovered.object.clone();
            //             hoveredScene?.add(hoveredClone);

            //             this.renderOnce();
            //         }
            //     }
            // } else {
            //     if (hovered) {
            //         hovered = undefined;
            //         hoveredScene = undefined;
            //         this.props.onHover(-1);
            //         this.renderOnce();
            //     }
            // }
        }
    };

    const buildObject3D = (primitive: Primitive, index: Index) => {
        let matrix: THREE.Matrix4;
        let geometry: THREE.BufferGeometry;
        switch(primitive.type)
        {
            case 4: matrix = new THREE.Matrix4().fromArray([primitive.radius, 0, 0, primitive.center[0], 0, primitive.radius, 0, primitive.center[1], 0, 0, primitive.radius, primitive.center[2], 0, 0, 0, 1]).transpose(); break;
            default: matrix = new THREE.Matrix4().fromArray([...primitive.affineTransform, 0, 0, 0, 1]).transpose(); break;
        }

        let material = singlePlantMaterial;
        switch(primitive.type)
        {
            case 1: geometry = threeCirclePrimitive; break;
            case 2: geometry = threeCylinderPrimitive;
                    matrix = matrix
                    .scale(new THREE.Vector3(primitive.radius, primitive.length, primitive.radius))
                    .setPosition(new THREE.Vector3(primitive.affineTransform[3] + matrix.elements[4] * 0.5, primitive.affineTransform[7] + matrix.elements[5] * 0.5, primitive.affineTransform[11] + matrix.elements[6] * 0.5));
                    break;
            case 4: geometry = threeSpherePrimitive; break;
            case 8: geometry = threePlanePrimitive;
                    matrix = matrix.setPosition(new THREE.Vector3(primitive.affineTransform[3] - primitive.affineTransform[0] * 0.5, primitive.affineTransform[7] - primitive.affineTransform[4] * 0.5, primitive.affineTransform[11] - primitive.affineTransform[8] * 0.5));
                    material = doublePlantMaterial;
                    break;
        }

        const mesh = new THREE.Mesh(geometry, material);
        mesh.name = `${index.entity.toString()}_${index.primitive.toString()}`;
        //mesh.layers.enable(1);
        mesh.applyMatrix4(matrix);
        mesh.updateMatrix();
        mesh.matrixAutoUpdate = false;
        let p = new THREE.Vector3();
        let q = new THREE.Quaternion();
        let s = new THREE.Vector3();
        matrix.decompose(p, q, s);
        console.log(p,q,s);
        scene.add(mesh);
    }

    //divContainer = createRef<HTMLDivElement>();
    //parent: HTMLElement | null | undefined;
    //let width: number;
    //let height: number;
    // let onHover: (id: number) => void;
    // let onDblClick: (id: number) => void;
    // let hasSelection: () => boolean;
    const scene = new THREE.Scene();
    const hemiLight = new THREE.HemisphereLight( 0xffffff, 0x707070, 3.175 );
    let perspectiveCamera: THREE.PerspectiveCamera;
    let renderer: THREE.WebGLRenderer;
    let controls: OrbitControls;

    let animationRequest: number;
    let mouse = { inside: false, x: 0, y: 0};

    const devicePixelRatio = window.devicePixelRatio || 1;

    useLayoutEffect(() => {
        //if (this.width !== this.props.width || this.height !== this.props.height) {
            console.log("Update SIZE");
            if (renderer)
                renderer.setSize(window.innerWidth, window.innerHeight);

            if ((window.innerWidth > 0 && window.innerHeight > 0) || !perspectiveCamera) {
                if (perspectiveCamera) {
                    perspectiveCamera.aspect = window.innerWidth / window.innerHeight;
                    //if (this.initData) perspectiveCamera.fov = ( 360 / Math.PI ) * Math.atan( this.initData.tanFOV * ( window.innerHeight / this.initData.windowHeight ) );
                    perspectiveCamera.updateProjectionMatrix();
                    controls?.update();
                }
                else
                    initCameras();
                renderOnce();
            }
            else
            {
                perspectiveCamera = undefined;
                controls?.dispose();
                controls = undefined;
            }
        // }
        // this.width = props.width;
        // this.height = props.height;

        return () =>
        {
            animationRequest && cancelAnimationFrame(animationRequest);
            animationRequest = undefined;
            //scene has/needs no dispose anymore
            scene.clear();
            controls?.dispose();
            controls = undefined;
        }
    }, [window.innerWidth, window.innerHeight]);


    effect(() => {
        initScene();
        const sceneData = appstate.scene.value;
        for(let i = 0; i < sceneData.length; ++i)
        {
            const entity = sceneData[i];
            for(let j = 0; j < entity.length; ++j)
                buildObject3D(entity[j], {entity: i, primitive: j});
        }
        renderOnce();
    });

    const divRef = useRef(null);

    useEffect(() => {
        if (WEBGL.isWebGL2Available()){
            const canvas = document.createElement('canvas');
            const context = canvas.getContext('webgl2', { alpha: false });
            if (context)
                renderer = new THREE.WebGLRenderer( { antialias: true, powerPreference: 'high-performance',  canvas: canvas, context: context as any } );
        }

        if (!renderer) {
            if ( WEBGL.isWebGLAvailable())
            {
                const canvas = document.createElement('canvas');
                const context = canvas.getContext('webgl', { alpha: false });
                renderer = new THREE.WebGLRenderer( { antialias: true, powerPreference: 'high-performance',  canvas: canvas, context: context as any } );
            }
            else
                divRef.current.appendChild(WEBGL.getWebGLErrorMessage());
        }

        initCameras();


        if (renderer && divRef.current) {
            renderer.setSize(window.innerWidth, window.innerHeight);
            divRef.current.appendChild(renderer.domElement);

            //this.renderer.gammaFactor = 2.2;
            //this.renderer.outputEncoding = THREE.GammaEncoding;
            renderer.outputColorSpace = THREE.SRGBColorSpace;
            renderer.useLegacyLights = true;

            renderer.setPixelRatio( devicePixelRatio );
            //this.renderer.setClearColor(new THREE.Color(0x1c1c1c));
            //this.renderer.toneMapping = THREE.NoToneMapping;
            renderer.toneMapping = THREE.LinearToneMapping;
            //this.renderer.toneMapping = THREE.ACESFilmicToneMapping;
            renderer.autoClear = false;
            initScene();
        }

        console.log("render Component");
        return () => divRef.current.removeChild(renderer.domElement);
    });


    return <div id="main3Dviewport" ref={divRef}></div>;
}

const threeBoxPrimitive = new THREE.BoxGeometry(1, 1, 1); //box
const threeSpherePrimitive = new THREE.SphereGeometry(1, 16, 16); //sphere
const threeCylinderPrimitive = new THREE.CylinderGeometry(1, 1, 1.0, 16); //cylinder
const threePlanePrimitive = new THREE.PlaneGeometry(1, 1); //rect
const threeCirclePrimitive = new THREE.CircleGeometry(1, 1); //disk

const singlePlantMaterial = new THREE.MeshStandardMaterial({ color: "#cccccc"});
const doublePlantMaterial = new THREE.MeshStandardMaterial({ color: "#cccccc", side: THREE.DoubleSide});