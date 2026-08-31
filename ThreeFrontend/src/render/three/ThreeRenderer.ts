//The Three.js/WebGL viewport backend.
//
//This is the former ThreeSceneFn component turned into a class behind IViewportRenderer.
//The rendering behaviour is intentionally the same as before - one Mesh per primitive, no
//instancing, render-on-demand - because this backend's job is to remain the parity reference
//and the compatibility fallback. What changed is where the decisions come from: geometry,
//colour, transforms and the draw list are all computed by src/render/core and merely
//realised here.

import * as THREE from 'three';
import { OrbitControls } from 'three/examples/jsm/controls/OrbitControls';
import { TransformControls } from 'three/examples/jsm/controls/TransformControls';
import WEBGL from 'three/examples/jsm/capabilities/WebGL';

import appstate from '../../appstate';
import { Seed } from '../../helpers/Seed';
import { Obstacle } from '../../helpers/Obstacle';
import { ITerrainItem, MeshTerrainItem, terrainPalette } from '../../helpers/Terrain';
import { BaseRequestObject } from '../../helpers/BaseRequestObject';
import { EncodePlantName } from '../../helpers/Plant';

import { CameraPose, PixelReadback, IViewportRenderer, PlantFrameInputs, TerrainFrameInputs, SeedFrameInputs, ObstacleFrameInputs, VisibilityInputs } from '../IViewportRenderer';
import { Clicks, DataRef, applyPick, toNDC } from '../core/Picking';
import { InstanceList, IFlags } from '../core/InstanceList';
import { buildPlants } from '../core/BuildScene';
import { VisualMappingOptions } from '../core/PlantColors';
import { Geom } from '../core/GeometryIds';
import { getGeometry, registerGeometry } from '../core/GeometryStore';
import { createMat4, Mat4 } from '../core/Mat4';
import { paletteColor } from '../core/SelectionPalette';
import { Ray } from '../core/Ray';
import { backgroundColor } from '../../helpers/Selection';
import { FrameLighting, SHADOW_SIZE, shadowBounds, SHADOW_NORMAL_OFFSET_TEXELS, SHADOW_GRAZING_OFFSET_TEXELS } from '../core/Lighting';
import { forceOpaque, reportOpaque, warnIfFlat } from '../offline/opaque';
import { OfflineSizeError } from '../offline/types';

import { toBufferGeometry } from './ThreeGeometry';
import { toThreeColor } from './ThreeColor';
import { applyInstanceMaterial, newInstanceMaterial } from './ThreeMaterials';
import '../core/StaticGeometry';   // side effect: fills the static geometry table

/** Per-mesh bookkeeping that has nowhere else to live. */
type MeshUserData = { ref?: DataRef; flags: number };

const terrainColor = paletteColor(terrainPalette, 'none');

export class ThreeRenderer implements IViewportRenderer {
    readonly kind = 'three' as const;

    private scene = new THREE.Scene();
    private renderer: THREE.WebGLRenderer | undefined;
    private camera: THREE.PerspectiveCamera | undefined;
    private controls: OrbitControls | undefined;
    private gizmo: TransformControls | undefined;
    private container: HTMLElement | undefined;

    private objSeeds = new THREE.Object3D();
    private objTerrain = new THREE.Object3D();
    private objObstacles = new THREE.Object3D();
    private objPlants = new THREE.Object3D();

    //Seeds and obstacles keep a stable Mesh per object rather than being rebuilt, because the
    //transform gizmo attaches to one and would be orphaned by a rebuild.
    private seedMeshes = new Map<Seed, THREE.Mesh>();
    private obstacleMeshes = new Map<Obstacle, THREE.Mesh>();
    private gizmoTarget: BaseRequestObject | undefined;

    private sun: THREE.DirectionalLight | undefined;
    private plantInstances = new InstanceList();
    private scratch: Mat4 = createMat4();
    private raycaster = new THREE.Raycaster();
    private disposed = false;
    /** What applyShadowFlags last pushed into the materials, so a no-op does not recompile. */
    private shadowsApplied = false;
    /** rAF handle for the coalesced render; 0 when none is queued. */
    private renderPending = 0;
    private hemiLight: THREE.HemisphereLight | undefined;

    //--- lifecycle -------------------------------------------------------------------------

    async mount(container: HTMLElement) {
        this.container = container;

        const canvas = document.createElement('canvas');
        //Ask for a context directly rather than trusting WEBGL.isWebGL2Available(): the
        //helper builds its own throwaway canvas and can disagree with what this one gets,
        //and a false negative there used to abort the mount on machines where WebGL works.
        //Whether a context can actually be created is the only question that matters.
        const context = (canvas.getContext('webgl2', { alpha: false })
            ?? canvas.getContext('webgl', { alpha: false })) as WebGLRenderingContext | null;

        if (!context) {
            container.appendChild(WEBGL.getWebGLErrorMessage());
            throw new Error('WebGL is not available');
        }

        this.renderer = new THREE.WebGLRenderer({
            antialias: true,
            powerPreference: 'high-performance',
            canvas,
            context: context as any,
        });

        this.renderer.outputColorSpace = THREE.SRGBColorSpace;
        this.renderer.useLegacyLights = true;
        this.renderer.toneMapping = THREE.LinearToneMapping;
        this.renderer.setPixelRatio(window.devicePixelRatio || 1);
        //autoClear stays on: the previous code disabled it and relied on scene.background
        //forcing a clear anyway (WebGLBackground sets forceClear for a Color background),
        //which worked but only by accident.
        this.renderer.autoClear = true;

        container.appendChild(this.renderer.domElement);

        this.scene.background = backgroundColor;
        //Intensity stays at 1 and the colours carry the energy, exactly as the sun does. That
        //is what lets setLighting swap in the daylight sky without a second scaling rule, and
        //it keeps the two backends reading their hemisphere from the same numbers.
        this.hemiLight = new THREE.HemisphereLight(0xffffff, 0x707070, 1);
        this.hemiLight.position.set(0, 10000, 0);
        this.scene.add(this.hemiLight);

        //Added dark and disabled, so Tier 0 is byte-identical to the historical renderer:
        //an intensity of zero contributes nothing, and shadowMap.enabled stays off until a
        //tier that wants shadows asks for it.
        this.sun = new THREE.DirectionalLight(0xffffff, 0);
        this.sun.visible = false;
        this.sun.castShadow = true;
        this.sun.shadow.mapSize.set(SHADOW_SIZE, SHADOW_SIZE);
        //Acne on thin foliage is far worse than the slight peter-panning these cost.
        //Both shadow biases are set per frame from the shadow bounds; see setLighting.
        this.scene.add(this.sun, this.sun.target);
        this.renderer.shadowMap.enabled = false;
        this.renderer.shadowMap.type = THREE.PCFSoftShadowMap;
        this.scene.add(this.objSeeds, this.objTerrain, this.objObstacles, this.objPlants);

        this.camera = new THREE.PerspectiveCamera(50, this.aspect(), 0.025, 5000);
        this.camera.position.set(0, 3, 5);

        this.controls = new OrbitControls(this.camera, this.renderer.domElement);
        this.controls.enableRotate = true;
        this.controls.screenSpacePanning = true;
        this.controls.addEventListener('change', this.renderOnce);
        this.controls.update();

        this.gizmo = new TransformControls(this.camera, this.renderer.domElement);
        this.gizmo.addEventListener('change', this.onGizmoChange);
        this.gizmo.addEventListener('dragging-changed', (e: any) => {
            if (this.controls) this.controls.enabled = !e.value;
        });
        this.scene.add(this.gizmo);

        this.resize(container.clientWidth || window.innerWidth, container.clientHeight || window.innerHeight, window.devicePixelRatio || 1);
    }

    dispose() {
        //Cancel the coalesced render before the context goes away, or the callback runs
        //against a disposed renderer.
        if (this.renderPending !== 0) { cancelAnimationFrame(this.renderPending); this.renderPending = 0; }
        this.disposed = true;
        this.controls?.removeEventListener('change', this.renderOnce);
        this.controls?.dispose();
        this.gizmo?.removeEventListener('change', this.onGizmoChange);
        this.gizmo?.detach();
        this.gizmo?.dispose();

        this.disposeGroup(this.objPlants);
        this.disposeGroup(this.objSeeds);
        this.disposeGroup(this.objObstacles);
        this.disposeGroup(this.objTerrain);
        this.seedMeshes.clear();
        this.obstacleMeshes.clear();

        const dom = this.renderer?.domElement;
        if (dom && dom.parentNode === this.container) this.container?.removeChild(dom);
        this.renderer?.dispose();

        this.renderer = undefined;
        this.camera = undefined;
        this.controls = undefined;
        this.gizmo = undefined;
        this.container = undefined;
        this.scene.clear();
    }

    private disposeGroup(group: THREE.Object3D) {
        group.traverse(o => {
            const m = (o as THREE.Mesh).material;
            if (Array.isArray(m)) m.forEach(x => x.dispose());
            else m?.dispose();
        });
        group.clear();
    }

    private aspect() {
        const w = this.container?.clientWidth || window.innerWidth;
        const h = this.container?.clientHeight || window.innerHeight;
        return h > 0 ? w / h : 1;
    }

    resize(cssWidth: number, cssHeight: number, dpr: number) {
        if (!this.renderer || cssWidth <= 0 || cssHeight <= 0) return;
        this.renderer.setPixelRatio(dpr);
        this.renderer.setSize(cssWidth, cssHeight);
        if (this.camera) {
            this.camera.aspect = cssWidth / cssHeight;
            this.camera.updateProjectionMatrix();
        }
        this.controls?.update();
        this.renderOnce();
    }

    /**
     * Coalesced to one render per animation frame.
     *
     * Several effects each ask for a render in the same tick - a plant rebuild, then the
     * lighting that follows it as the playhead advances - and every render also redraws the
     * 4096 shadow map over the entire scene. Rendering per request meant two full scene passes
     * per simulated timestep, which grew with the plant until the driver gave up: Chromium on
     * Vulkan reported it as a link failure with an empty log, then lost the context.
     *
     * Nothing needs a synchronous render: picking uses a raycaster and the offline path renders
     * into its own target.
     */
    renderOnce = () => {
        if (this.disposed || !this.renderer || !this.camera) return;
        if (this.renderPending !== 0) return;
        this.renderPending = requestAnimationFrame(() => {
            this.renderPending = 0;
            if (this.disposed || !this.renderer || !this.camera) return;
            this.renderer.render(this.scene, this.camera);
        });
    };

    //--- camera ----------------------------------------------------------------------------

    getCameraPose(): CameraPose {
        const c = this.camera;
        const t = this.controls?.target;
        return {
            position: c ? { x: c.position.x, y: c.position.y, z: c.position.z } : { x: 0, y: 3, z: 5 },
            target: t ? { x: t.x, y: t.y, z: t.z } : { x: 0, y: 0, z: 0 },
            up: c ? { x: c.up.x, y: c.up.y, z: c.up.z } : { x: 0, y: 1, z: 0 },
            fovY: c?.fov ?? 50,
            near: c?.near ?? 0.025,
            far: c?.far ?? 5000,
        };
    }

    setCameraPose(p: CameraPose) {
        if (!this.camera) return;
        this.camera.position.set(p.position.x, p.position.y, p.position.z);
        this.camera.up.set(p.up.x, p.up.y, p.up.z);
        this.camera.fov = p.fovY;
        this.camera.near = p.near;
        this.camera.far = p.far;
        this.camera.updateProjectionMatrix();
        if (this.controls) {
            this.controls.target.set(p.target.x, p.target.y, p.target.z);
            this.controls.update();
        }
        this.renderOnce();
    }

    //--- geometry resolution ----------------------------------------------------------------

    private geometryFor(id: number): THREE.BufferGeometry {
        const data = getGeometry(id);
        //A dynamic id can be referenced before its MeshData has been registered (a species
        //whose leaf mesh is still being fetched); fall back to the flat plane, which is what
        //the previous `leafGeometry ?? threePlanePrimitive` did.
        return toBufferGeometry(data ?? getGeometry(Geom.Plane)!);
    }

    //--- plants -----------------------------------------------------------------------------

    rebuildPlants(inputs: PlantFrameInputs) {
        buildPlants(this.plantInstances, inputs);
        //syncInstanceMeshes sets each mesh's shadow flags as it goes, so no traverse here.
        this.syncInstanceMeshes(this.objPlants, this.plantInstances);
        this.renderOnce();
    }

    /**
     * Pools one Mesh per instance slot, mutating in place.
     *
     * Slot order is the instance order, which is also what the WebGPU picker uses as its id
     * space - keeping the two aligned is what makes picking behave identically on both
     * backends.
     */
    /**
     * Reapplies the current shadow flags to meshes that were built after the last tier change.
     *
     * `castShadow` and `receiveShadow` live on the Mesh, and every rebuild makes new ones - so
     * without this a plant that grew after shadows were switched on silently stopped casting.
     * `changed` is false because the materials' programs already carry shadow support; only
     * the per-object flags are missing.
     */
    private reapplyShadowFlags() {
        if (this.shadowsApplied) this.applyShadowFlags(true, false);
    }

    private syncInstanceMeshes(group: THREE.Object3D, list: InstanceList) {
        const children = group.children as THREE.Mesh[];

        for (let i = 0; i < list.count; ++i) {
            const inst = list.items[i];
            let mesh = children[i];

            if (!mesh) {
                mesh = new THREE.Mesh(this.geometryFor(inst.geom), newInstanceMaterial());
                mesh.matrixAutoUpdate = false;
                mesh.userData = { flags: -1 } as MeshUserData;
                group.add(mesh);
            }

            const ud = mesh.userData as MeshUserData;
            const geometry = this.geometryFor(inst.geom);
            if (mesh.geometry !== geometry) mesh.geometry = geometry;

            applyInstanceMaterial(mesh.material as THREE.MeshStandardMaterial, inst.color, inst.emissive, inst.flags, ud.flags);

            //Set here rather than by a separate traverse afterwards: the flags live on the
            //Mesh, every rebuild makes new ones, and this loop already touches each one. Two
            //full-scene traverses per timestep is exactly the cost that was sinking this.
            mesh.castShadow = this.shadowsApplied;
            mesh.receiveShadow = this.shadowsApplied;

            mesh.matrix.fromArray(inst.model);
            mesh.matrixWorldNeedsUpdate = true;
            mesh.visible = (inst.flags & IFlags.Hidden) === 0;
            mesh.name = inst.ref?.type === 'plant' ? EncodePlantName(inst.ref.index) : '';

            ud.ref = inst.ref;
            ud.flags = inst.flags;
        }

        //Surplus meshes from a previous, larger frame.
        if (children.length > list.count) {
            const removed = children.splice(list.count, children.length - list.count);
            removed.forEach(m => {
                const mat = m.material;
                if (Array.isArray(mat)) mat.forEach(x => x.dispose());
                else mat?.dispose();
            });
        }
    }

    /**
     * Applies the frame's lighting.
     *
     * The ~100 lines this needs are why the shadow map is worth porting to the WebGL backend
     * at all: it is the single largest perceptual gain in the whole lighting ladder, and this
     * way it reaches everyone rather than only machines with WebGPU.
     */
    setLighting(lighting: FrameLighting) {
        if (!this.sun || !this.renderer) return;

        //three multiplies a hemisphere light's colours by its intensity, which is 1 here, so
        //these ARE the irradiance values the WebGPU frame uniform carries.
        this.hemiLight?.color.setRGB(lighting.hemiSky.r, lighting.hemiSky.g, lighting.hemiSky.b);
        this.hemiLight?.groundColor.setRGB(lighting.hemiGround.r, lighting.hemiGround.g, lighting.hemiGround.b);

        //The sun colour is already black whenever the sun should not contribute - the tier is
        //off, or it is below the horizon - so this is the whole test.
        const wantsSun = lighting.sunColor.r + lighting.sunColor.g + lighting.sunColor.b > 0;
        this.sun.visible = wantsSun;
        this.renderer.shadowMap.enabled = wantsSun && lighting.shadows;

        if (wantsSun) {
            //three scales a directional light's colour by intensity, so folding the intensity
            //into the colour (as the neutral core does) means intensity stays at 1.
            this.sun.color.setRGB(lighting.sunColor.r, lighting.sunColor.g, lighting.sunColor.b);
            this.sun.intensity = 1;

            const { center, radius } = shadowBounds(appstate.fieldSizeX.peek(), appstate.fieldSizeZ.peek());

            //Both biases come from the same texel count, derived from the texel's world size
            //rather than hard-coded, so they stay correct if the field or the map resolution
            //changes and the one control moves both.
            //
            //Why BOTH: three's normalBias offsets along the surface normal as stored, and it
            //does not flip that normal towards the light. For a leaf lit from behind - which
            //this renderer deliberately lights, via abs(dot(n, L)), so canopies do not go
            //black in patches - the offset therefore pushes the lookup DEEPER into shadow and
            //makes self-shadowing worse. A plain depth bias has no direction to get wrong, so
            //it is what actually covers that case. The WebGPU path flips the normal instead
            //and needs no equivalent.
            const texelWorld = (2 * radius) / SHADOW_SIZE;
            this.sun.shadow.normalBias = texelWorld * (lighting.shadowBiasTexels + SHADOW_GRAZING_OFFSET_TEXELS * 0.5);
            const d = lighting.sunDirection;
            this.sun.position.set(
                center.x + d.x * radius * 2,
                center.y + d.y * radius * 2,
                center.z + d.z * radius * 2);
            this.sun.target.position.set(center.x, center.y, center.z);
            this.sun.target.updateMatrixWorld();

            //Fitted to the field, not the plants: a frustum that resized as they grew would
            //make the shadow map swim between timesteps.
            const cam = this.sun.shadow.camera;
            cam.left = -radius; cam.right = radius;
            cam.top = radius; cam.bottom = -radius;
            cam.near = 0.1; cam.far = radius * 4;
            //Normalised depth spans the shadow camera's near..far, so a world offset converts
            //by dividing through that range. Negative: three ADDS the bias before comparing,
            //and moving the comparison towards the light is what avoids self-shadowing.
            const depthRange = Math.max(1e-6, cam.far - cam.near);
            this.sun.shadow.bias = -(texelWorld * lighting.shadowBiasTexels) / depthRange;
            cam.updateProjectionMatrix();
        }

        const shadows = wantsSun && lighting.shadows;
        //Only traverse when the answer actually changed. setLighting now runs on every
        //simulated timestep, because the sun follows the clock, and walking every mesh in a
        //growing canopy each time is pure waste - the per-mesh flags are set during the
        //rebuild that created those meshes.
        if (shadows !== this.shadowsApplied) {
            this.shadowsApplied = shadows;
            this.applyShadowFlags(shadows, true);
        }
        this.renderOnce();
    }

    /**
     * Turns shadow casting on or off across the scene.
     *
     * Leaves are single double-sided quads, so `shadowSide = DoubleSide` is required or half
     * the canopy casts nothing.
     */
    /**
     * @param changed Whether shadows just turned on or off, as opposed to merely being
     *   reapplied to freshly built meshes.
     *
     * three compiles shadow support INTO each material's program, so flipping
     * `renderer.shadowMap.enabled` or a mesh's `receiveShadow` after a program is cached does
     * nothing until that program is rebuilt. Without `needsUpdate` the switch appeared to do
     * nothing until the next timestep, when `buildPlants` happened to create new materials -
     * which is exactly what "it only changes once a frame moves" looked like. It is set only
     * on a real change because recompiling every material is not free.
     */
    private applyShadowFlags(enabled: boolean, changed: boolean) {
        const apply = (group: THREE.Object3D) => group.traverse(o => {
            const mesh = o as THREE.Mesh;
            if (!mesh.isMesh) return;
            mesh.castShadow = enabled;
            mesh.receiveShadow = enabled;
            const material = mesh.material as THREE.Material | THREE.Material[];
            const one = (m: THREE.Material) => {
                m.shadowSide = THREE.DoubleSide;
                if (changed) m.needsUpdate = true;
            };
            if (Array.isArray(material)) material.forEach(one);
            else if (material) one(material);
        });

        apply(this.objPlants);
        apply(this.objTerrain);
        apply(this.objObstacles);
        apply(this.objSeeds);
    }

    refreshVisibility(inputs: VisibilityInputs) {
        this.objTerrain.visible = inputs.terrain;
        this.objSeeds.visible = inputs.seeds;
        this.objObstacles.visible = inputs.obstacles;
        this.renderOnce();
    }

    //--- terrain ----------------------------------------------------------------------------

    rebuildTerrain(inputs: TerrainFrameInputs) {
        const list = inputs.items as ITerrainItem[];
        const material = new THREE.MeshLambertMaterial({
            color: toThreeColor(terrainColor),
            side: THREE.DoubleSide,
            flatShading: true,
        });

        this.disposeGroup(this.objTerrain);

        if (list?.length > 0) {
            for (let i = 0; i < list.length; ++i) {
                const t = list[i];
                if (t instanceof MeshTerrainItem && t.meshData)
                    t.meshGeom = registerGeometry(`terrain:${i}:${t.id}`, t.meshData);

                const mesh = new THREE.Mesh(this.geometryFor(t.geomId()), material);
                mesh.matrixAutoUpdate = false;
                mesh.matrix.fromArray(t.writeModel(this.scratch));
                mesh.matrixWorldNeedsUpdate = true;
                mesh.userData = { ref: { type: 'terrain', index: i, terrain: t }, flags: 0 } as MeshUserData;
                this.objTerrain.add(mesh);
            }
        }
        else {
            //No imported terrain: one box sized by the configured field.
            const w = inputs.fieldSizeX;
            const d = inputs.fieldSizeD;
            const l = inputs.fieldSizeZ;

            const mesh = new THREE.Mesh(this.geometryFor(Geom.BoxCorner), material);
            mesh.scale.set(w, d, l);
            mesh.position.set(0, -d, 0);
            mesh.userData = { ref: { type: 'terrain' }, flags: 0 } as MeshUserData;
            this.objTerrain.add(mesh);
        }

        this.objTerrain.visible = inputs.visible;
        this.reapplyShadowFlags();
        this.renderOnce();
    }

    //--- seeds and obstacles ------------------------------------------------------------------

    rebuildSeeds(inputs: SeedFrameInputs) {
        const seeds = inputs.seeds as Seed[];
        const live = new Set(seeds);

        for (const [seed, mesh] of this.seedMeshes) {
            if (!live.has(seed)) {
                this.objSeeds.remove(mesh);
                (mesh.material as THREE.Material).dispose();
                this.seedMeshes.delete(seed);
            }
        }

        for (const seed of seeds) {
            let mesh = this.seedMeshes.get(seed);
            if (!mesh) {
                mesh = new THREE.Mesh(this.geometryFor(Geom.Dodecahedron), new THREE.MeshLambertMaterial());
                mesh.userData = { ref: { type: 'seed', seed }, flags: 0 } as MeshUserData;
                this.objSeeds.add(mesh);
                this.seedMeshes.set(seed, mesh);
            }

            const p = seed.worldPosition();
            mesh.position.set(p.x, p.y, p.z);
            const c = paletteColor(seed.palette, seed.state.value);
            (mesh.material as THREE.MeshLambertMaterial).color.setRGB(c.r, c.g, c.b);
        }

        this.objSeeds.visible = inputs.visible;
        this.reapplyShadowFlags();
        this.renderOnce();
    }

    rebuildObstacles(inputs: ObstacleFrameInputs) {
        const obstacles = inputs.obstacles as Obstacle[];
        const live = new Set(obstacles);

        for (const [obstacle, mesh] of this.obstacleMeshes) {
            if (!live.has(obstacle)) {
                this.objObstacles.remove(mesh);
                (mesh.material as THREE.Material).dispose();
                this.obstacleMeshes.delete(obstacle);
            }
        }

        for (const obstacle of obstacles) {
            let mesh = this.obstacleMeshes.get(obstacle);
            if (!mesh) {
                mesh = new THREE.Mesh(this.geometryFor(Geom.BoxBottomCentered), new THREE.MeshLambertMaterial({
                    side: THREE.DoubleSide,
                    flatShading: true,
                    //Obstacles are frequently coplanar with the terrain they sit on.
                    polygonOffset: true,
                    polygonOffsetFactor: -2,
                }));
                mesh.matrixAutoUpdate = false;
                mesh.userData = { ref: { type: 'obstacle', obstacle }, flags: 0 } as MeshUserData;
                this.objObstacles.add(mesh);
                this.obstacleMeshes.set(obstacle, mesh);
            }

            if (obstacle.meshData)
                obstacle.meshGeom = registerGeometry(`obstacle:${obstacle.uid}`, obstacle.meshData);

            const geometry = this.geometryFor(obstacle.geomId());
            if (mesh.geometry !== geometry) mesh.geometry = geometry;

            mesh.matrix.fromArray(obstacle.writeModel(this.scratch));
            mesh.matrixWorldNeedsUpdate = true;

            const c = paletteColor(obstacle.palette, obstacle.state.value);
            (mesh.material as THREE.MeshLambertMaterial).color.setRGB(c.r, c.g, c.b);
        }

        this.objObstacles.visible = inputs.visible;
        this.reapplyShadowFlags();
        this.renderOnce();
    }

    //--- picking -----------------------------------------------------------------------------

    pick(clientX: number, clientY: number, clicks: Clicks) {
        if (!this.renderer || !this.camera || !appstate.performPicking.peek()) return;

        const dom = this.renderer.domElement;
        const rect = dom.getBoundingClientRect();
        const ndc = toNDC(clientX, clientY, rect);

        const origin = this.camera.position;
        const dir = new THREE.Vector3(ndc.x, ndc.y, 1).unproject(this.camera).sub(origin).normalize();
        this.raycaster.set(origin, dir);

        const ray: Ray = {
            o: { x: origin.x, y: origin.y, z: origin.z },
            d: { x: dir.x, y: dir.y, z: dir.z },
        };

        //Objects being dragged follow the cursor regardless of what is under it.
        appstate.grabbed.value?.forEach((s: Seed) => s.move(ray));

        const hits: THREE.Intersection[] = [];
        if (appstate.showSeeds.peek()) hits.push(...this.raycaster.intersectObjects(this.objSeeds.children, true));
        if (appstate.showObstacles.peek()) hits.push(...this.raycaster.intersectObjects(this.objObstacles.children, true));
        hits.push(...this.raycaster.intersectObjects(this.objPlants.children.filter(x => x.visible), true));
        if (appstate.showTerrain.peek()) hits.push(...this.raycaster.intersectObjects(this.objTerrain.children, true));
        hits.sort((a, b) => a.distance - b.distance);

        const ref = hits.length > 0 ? (hits[0].object.userData as MeshUserData)?.ref : undefined;
        applyPick(ref, clicks, ray);
    }

    pickCancel() {
        applyPick(undefined, Clicks.None, { o: { x: 0, y: 0, z: 0 }, d: { x: 0, y: 0, z: 1 } });
    }

    //--- gizmo --------------------------------------------------------------------------------

    attachGizmo(target: unknown) {
        const obj = target as BaseRequestObject;
        const mesh = this.meshForSelectable(obj);
        if (!mesh || !this.gizmo) return;
        this.gizmoTarget = obj;
        this.gizmo.attach(mesh);
        this.renderOnce();
    }

    detachGizmo(target: unknown) {
        if (!this.gizmo) return;
        const mesh = this.meshForSelectable(target as BaseRequestObject);
        if (mesh && this.gizmo.object === mesh) {
            this.gizmo.detach();
            this.gizmoTarget = undefined;
        }
        this.renderOnce();
    }

    private meshForSelectable(obj: BaseRequestObject): THREE.Mesh | undefined {
        if (obj instanceof Seed) return this.seedMeshes.get(obj);
        if (obj instanceof Obstacle) return this.obstacleMeshes.get(obj);
        return undefined;
    }

    private onGizmoChange = () => {
        const target = this.gizmoTarget;
        const mesh = this.gizmo?.object as THREE.Mesh | undefined;
        if (target && mesh) {
            //The gizmo moves the mesh; push that back into the model, which is the source of
            //truth. Subclasses undo whatever offset they apply when drawing.
            target.transformMove(mesh.position.x, mesh.position.y, mesh.position.z);
        }
        this.renderOnce();
    };

    //--- offscreen rendering ---------------------------------------------------------------------

    /**
     * Renders the current scene at an arbitrary size, independent of the canvas.
     *
     * Satisfies the PixelReadback contract in IViewportRenderer: RGBA8, top-left row origin,
     * straight alpha, already sRGB-encoded, box-averaged in encoded sRGB.
     */
    async renderToPixels(width: number, height: number, ssaa: number, msaa: 1 | 4, pose: CameraPose): Promise<PixelReadback> {
        const renderer = this.renderer;
        if (!renderer) throw new Error('renderToPixels called before the viewport was mounted');

        const rw = Math.max(1, Math.round(width * ssaa));
        const rh = Math.max(1, Math.round(height * ssaa));

        const gl = renderer.getContext();
        const max = Math.min(gl.getParameter(gl.MAX_TEXTURE_SIZE), gl.getParameter(gl.MAX_RENDERBUFFER_SIZE));
        if (rw > max || rh > max) throw new OfflineSizeError(rw, rh, max);

        const savedTarget = renderer.getRenderTarget();
        const savedAutoClear = renderer.autoClear;

        const big = new THREE.WebGLRenderTarget(rw, rh, {
            minFilter: THREE.LinearFilter,
            magFilter: THREE.LinearFilter,
            format: THREE.RGBAFormat,
            type: THREE.UnsignedByteType,
            depthBuffer: true,
            stencilBuffer: false,
            samples: msaa > 1 ? msaa : 0,
        });
        //THE bug to avoid: renderer.outputColorSpace applies only to the canvas. Without this
        //the offscreen image comes out darker than the viewport, because the shader would
        //write linear values where the canvas gets sRGB-encoded ones.
        big.texture.colorSpace = THREE.SRGBColorSpace;
        big.texture.generateMipmaps = false;

        let small: THREE.WebGLRenderTarget | undefined;
        let resolve: ResolvePass | undefined;

        try {
            const camera = new THREE.PerspectiveCamera(pose.fovY, width / height, pose.near, pose.far);
            camera.position.set(pose.position.x, pose.position.y, pose.position.z);
            camera.up.set(pose.up.x, pose.up.y, pose.up.z);
            camera.lookAt(pose.target.x, pose.target.y, pose.target.z);
            camera.updateProjectionMatrix();

            renderer.autoClear = true;
            renderer.setRenderTarget(big);
            renderer.clear(true, true, false);
            renderer.render(this.scene, camera);

            let source = big;
            if (ssaa > 1) {
                small = new THREE.WebGLRenderTarget(width, height, {
                    minFilter: THREE.NearestFilter,
                    magFilter: THREE.NearestFilter,
                    format: THREE.RGBAFormat,
                    type: THREE.UnsignedByteType,
                    depthBuffer: false,
                    stencilBuffer: false,
                });
                //Linear-sRGB here means "no conversion": the bytes are already encoded and
                //must pass through the resolve pass untouched.
                small.texture.colorSpace = THREE.LinearSRGBColorSpace;

                resolve = makeResolvePass(ssaa, rw, rh);
                resolve.material.uniforms.tSrc.value = big.texture;
                renderer.setRenderTarget(small);
                renderer.render(resolve.scene, resolve.camera);
                source = small;
            }

            //Unbinding first forces the MSAA resolve blit before the read.
            renderer.setRenderTarget(null);

            const data = new Uint8ClampedArray(source.width * source.height * 4);
            renderer.readRenderTargetPixels(source, 0, 0, source.width, source.height, data);

            //The resolve pass already flipped Y in the shader; the direct path has not, and
            //WebGL readPixels is bottom-up.
            if (ssaa <= 1) flipRowsInPlace(data, source.width, source.height);

            reportOpaque(forceOpaque(data), data.length / 4, 'three');
            warnIfFlat(data, source.width, source.height, 'three');
            return { data, width: source.width, height: source.height };
        } finally {
            renderer.setRenderTarget(savedTarget);
            renderer.autoClear = savedAutoClear;
            big.dispose();
            small?.dispose();
            resolve?.dispose();
            this.renderOnce();
        }
    }

    //--- export --------------------------------------------------------------------------------

    exportGLTF(save: (json: string) => void) {
        //Dynamically imported so the exporter is not in the main bundle; webpack 4 supports
        //this and emits a separate chunk.
        import('three/examples/jsm/exporters/GLTFExporter').then(({ GLTFExporter }) => {
            new GLTFExporter().parse(
                this.scene,
                gltf => save(JSON.stringify(gltf)),
                error => console.error('glTF export failed', error),
                {},
            );
        });
    }
}


//--- offscreen resolve pass -------------------------------------------------------------------

type ResolvePass = {
    scene: THREE.Scene;
    camera: THREE.OrthographicCamera;
    material: THREE.ShaderMaterial;
    dispose(): void;
};

/**
 * Full-screen pass that box-averages an ssaa x ssaa block AND flips Y.
 *
 * Doing both on the GPU means only the OUTPUT resolution is read back, which at 4K with 2x
 * supersampling is a quarter of the bandwidth, and saves a separate CPU flip.
 *
 * A ShaderMaterial is used rather than a built-in one precisely because three injects no
 * colour-space conversion into custom shaders: the source bytes are already sRGB-encoded and
 * must be averaged and written through unchanged.
 */
function makeResolvePass(ssaa: number, srcW: number, srcH: number): ResolvePass {
    const material = new THREE.ShaderMaterial({
        uniforms: {
            tSrc: { value: null },
            texelSize: { value: new THREE.Vector2(1 / srcW, 1 / srcH) },
            n: { value: ssaa },
        },
        vertexShader: `
            varying vec2 vUv;
            void main() {
                vUv = uv;
                gl_Position = vec4(position.xy, 0.0, 1.0);
            }
        `,
        fragmentShader: `
            uniform sampler2D tSrc;
            uniform vec2 texelSize;
            uniform int n;
            varying vec2 vUv;
            void main() {
                // WebGL render targets are bottom-up; VideoFrame and ImageData are top-down.
                vec2 uv = vec2(vUv.x, 1.0 - vUv.y);
                vec4 acc = vec4(0.0);
                float count = 0.0;
                for (int y = 0; y < 8; ++y) {
                    if (y >= n) break;
                    for (int x = 0; x < 8; ++x) {
                        if (x >= n) break;
                        vec2 offset = (vec2(float(x), float(y)) + 0.5 - float(n) * 0.5) * texelSize;
                        acc += texture2D(tSrc, uv + offset);
                        count += 1.0;
                    }
                }
                gl_FragColor = acc / count;
            }
        `,
        depthTest: false,
        depthWrite: false,
    });

    const geometry = new THREE.PlaneGeometry(2, 2);
    const scene = new THREE.Scene();
    scene.add(new THREE.Mesh(geometry, material));

    return {
        scene,
        camera: new THREE.OrthographicCamera(-1, 1, 1, -1, 0, 1),
        material,
        dispose() { geometry.dispose(); material.dispose(); },
    };
}

/** WebGL readPixels returns rows bottom-up; PixelReadback requires top-down. */
function flipRowsInPlace(data: Uint8ClampedArray, width: number, height: number) {
    const stride = width * 4;
    const row = new Uint8ClampedArray(stride);
    for (let y = 0; y < Math.floor(height / 2); ++y) {
        const top = y * stride;
        const bottom = (height - 1 - y) * stride;
        row.set(data.subarray(top, top + stride));
        data.copyWithin(top, bottom, bottom + stride);
        data.set(row, bottom);
    }
}
