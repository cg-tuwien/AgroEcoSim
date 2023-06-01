import { PureComponent, createRef, findDOMNode } from "preact/compat";
import { h } from 'preact';
import * as THREE from "three";
import { OrbitControls } from "three/examples/jsm/controls/OrbitControls"
import { FlyControls } from "three/examples/jsm/controls/FlyControls"
import * as WEBGL from "three/examples/jsm/capabilities/WebGL"

interface IProps {
    width: number,
    height: number,
    onHover(id: number): void,
    onDblClick(id: number): void,
    hasSelection(): boolean,
};

interface IInitData {
    tanFOV: number;
    windowHeight: number;
}

export default class ThreeScene extends PureComponent<IProps> {
    divContainer = createRef<HTMLDivElement>();
    parent: HTMLElement | null | undefined;

    scene?: THREE.Scene;
    perspectiveCamera?: THREE.PerspectiveCamera;
    renderer?: THREE.WebGLRenderer;
    controls?: OrbitControls;

    hovered?: THREE.Intersection;
    hoveredScene?: THREE.Scene;

    animationRequest?: number;
    mouse = { inside: false, x: 0, y: 0};

    initData?: IInitData;

    materialHovered = new THREE.MeshBasicMaterial({
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

    public componentDidMount = () => {
        if (this.props.width > 0 && this.props.height > 0)
            this.initCameras();

        if (WEBGL.isWebGL2Available() && this.divContainer.current){
            const canvas = document.createElement('canvas');
            const context = canvas.getContext('webgl2', { alpha: false });
            if (context)
                this.renderer = new THREE.WebGLRenderer( { antialias: true, powerPreference: 'high-performance',  canvas: canvas, context: context as any} );
        }

        if (!this.renderer) {
            if ( WEBGL.isWebGLAvailable())
                this.renderer = new THREE.WebGLRenderer( { antialias: true, powerPreference: 'high-performance' } );
            else
                this.divContainer.current?.appendChild( WEBGL.getWebGLErrorMessage() );
        }

        const devicePixelRatio = window.devicePixelRatio || 1;

        if (this.renderer) {
            this.renderer.setSize(this.props.width, this.props.height);
            this.divContainer.current?.appendChild(this.renderer.domElement);

            //this.renderer.gammaFactor = 2.2;
            //this.renderer.outputEncoding = THREE.GammaEncoding;
            this.renderer.outputEncoding = THREE.sRGBEncoding;
            this.renderer.physicallyCorrectLights = true;

            this.renderer.setPixelRatio( devicePixelRatio );
            //this.renderer.setClearColor(new THREE.Color(0xffffff));
            //this.renderer.toneMapping = THREE.NoToneMapping;
            this.renderer.toneMapping = THREE.LinearToneMapping;
            //this.renderer.toneMapping = THREE.ACESFilmicToneMapping;
            this.renderer.autoClear = false;
            this.initScene();
        }

        this.parent = findDOMNode(this)?.parentElement;
    }

    private renderOnce() {
        if (this.scene && this.perspectiveCamera && this.renderer) {
            this.renderer.render(this.scene, this.perspectiveCamera);
            // if (this.hoveredScene)
            //     this.renderer.render(this.hoveredScene, this.perspectiveCamera);
        }
    }

    public componentDidUpdate = (prevProps: IProps) => {
        if (prevProps.width !== this.props.width || prevProps.height !== this.props.height) {
            if (this.renderer)
                this.renderer.setSize(this.props.width, this.props.height);

            if (this.props.width > 0 && this.props.height > 0) {
                if (this.perspectiveCamera) {
                    this.perspectiveCamera.aspect = this.props.width / this.props.height;
                    if (this.initData)
                        this.perspectiveCamera.fov = ( 360 / Math.PI ) * Math.atan( this.initData.tanFOV * ( this.props.height / this.initData.windowHeight ) );
                    this.perspectiveCamera.updateProjectionMatrix();
                    this.controls?.update();
                }
                else
                    this.initCameras();
                this.renderOnce();
            }
            else
            {
                this.perspectiveCamera = undefined;
                this.controls?.dispose();
                this.controls = undefined;
            }
        }
    }

    private initCameras() {
        this.perspectiveCamera = new THREE.PerspectiveCamera(50, this.props.width / this.props.height, 0.025, 5000);
        this.perspectiveCamera.position.set(0, 20, 30);
        if (this.renderer){
            this.controls = new OrbitControls(this.perspectiveCamera, this.renderer.domElement);
            this.controls.enableKeys = false;
            this.controls.screenSpacePanning = true;

            //this.controls.addEventListener("start", () => { this.performRendering = true });
            //this.controls.addEventListener("end", () => { this.performRendering = false });
            this.controls.addEventListener("change", () => { this.renderOnce(); });
            this.renderer?.domElement.addEventListener('wheel', () => { this.renderOnce(); });
        }

        this.initData = { tanFOV: Math.tan( ( ( Math.PI / 180 ) * this.perspectiveCamera.fov / 2 ) ), windowHeight: this.props.height };
    }

    private initScene(){
        this.scene = new THREE.Scene();
        this.scene.background = new THREE.Color("#FFFFFF");

        const hemiLight = new THREE.HemisphereLight( 0xffffff, 0x707070, 3.175 );
        // hemiLight.color.setHSL( 0.6, 1, 0.6 );
        // hemiLight.groundColor.setHSL( 0.2, 0.2, 0.2 );
        hemiLight.position.set( 0, 10000, 0 );
        this.scene?.add( hemiLight );
    }

    public componentWillUnmount = () =>
    {
        this.animationRequest && cancelAnimationFrame(this.animationRequest);
        this.animationRequest = undefined;
        //scene has/needs no dispose anymore
        this.scene = undefined;
        this.controls?.dispose();
        this.controls = undefined;
    }

    public render() {
        return <div id="main3Dviewport" ref={this.divContainer} onMouseMove={this.onMouseMove.bind(this)} onDblClick={this.onDblClick.bind(this)}></div>
    }


    onMouseMove(event: MouseEvent) {
        this.interaction(event.clientX, event.clientY, false);
    }

    onDblClick(event: MouseEvent) {
        this.interaction(event.clientX, event.clientY, true);
    }

    interaction(x: number, y: number, isDblClick: boolean) {
        if (this.divContainer.current && this.perspectiveCamera) {
            const offsetLeft = this.divContainer.current.offsetLeft;
            const offsetTop = this.divContainer.current.offsetTop;

            if (x < offsetLeft || y < offsetTop ||
                x > offsetLeft + this.props.width || y > offsetTop + this.props.height)
                this.mouse.inside = false;
            else {
                this.mouse.x = ((x - offsetLeft) / this.props.width) * 2 - 1;
                this.mouse.y = 1 - ((y - offsetTop) / this.props.height) * 2;
                this.mouse.inside = true;

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
    }

    raycastScene(isDblClick: boolean) {
        if (this.mouse.inside && this.scene && this.perspectiveCamera) {
            const mousePoint = new THREE.Vector3(this.mouse.x, this.mouse.y, 1); //The mouse point in homogenous coordinates (1 at the end)
            mousePoint.unproject(this.perspectiveCamera);
            const raycaster = new THREE.Raycaster(this.perspectiveCamera.position, mousePoint.sub(this.perspectiveCamera.position).normalize());
            raycaster.layers.enable(1); //only Michelangelo's objects
            //raycaster.params.Line = { threshold: 0.04 }

            const intersections = raycaster.intersectObjects(this.scene.children, false);

            if (intersections.length > 0) {
                const closest = intersections[0];
                if (((this.hovered?.object.name != closest.object.name) || isDblClick))
                {
                    //scene has/needs no dispose anymore, thus no need for this.hoveredScene.dispose();
                    this.hoveredScene = new THREE.Scene();
                    this.hoveredScene.overrideMaterial = this.materialHovered;

                    this.hovered = closest;
                    if (this.hovered?.object)
                    {
                        const n = parseInt(this.hovered?.object.name);
                        this.props.onHover(n);
                        if (isDblClick)
                            this.props.onDblClick(n)

                        const hoveredClone = this.hovered.object.clone();
                        this.hoveredScene?.add(hoveredClone);

                        this.renderOnce();
                    }
                }
            } else {
                if (this.hovered) {
                    this.hovered = undefined;
                    this.hoveredScene = undefined;
                    this.props.onHover(-1);
                    this.renderOnce();
                }
            }
        }
    }

}