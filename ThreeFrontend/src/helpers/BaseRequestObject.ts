import { Signal, batch, signal } from "@preact/signals";
import { SelectionState } from "./Selection";
import appstate from "../appstate";
import { SelectionPalette } from "../render/core/SelectionPalette";
import { Ray, intersectPlaneY } from "../render/core/Ray";
import { Vec3 } from "../render/core/Vec3";
import { activeRenderer } from "../render/IViewportRenderer";

/**
 * Base for the editable objects in the scene: seeds, obstacles and terrain items.
 *
 * This class no longer owns a THREE.Mesh or any materials. It holds position, selection
 * state and the selection state machine; the mounted renderer is responsible for drawing
 * something at that position in the colour the palette specifies. That is what lets the same
 * FSM drive both the WebGL and the WebGPU viewport.
 */
export class BaseRequestObject {
    px: Signal<number>;
    py: Signal<number>;
    pz: Signal<number>;
    fieldIndex: Signal<number>; //this will become dangerous as soon as terrain items can be deleted or added
    state: Signal<SelectionState>;

    /**
     * Forces direct cursor dragging even when a gizmo is available.
     *
     * Historically always false, and nothing ever set it, so the plane-drag path below was
     * dead and all dragging went through the Three transform gizmo.
     */
    respondToMove = false;
    movable = true;

    palette: SelectionPalette;

    private grabOffset: Vec3 | undefined;

    /**
     * Whether this object should handle dragging itself.
     *
     * True when the mounted renderer offers no gizmo - the WebGPU backend does not - so that
     * seeds and obstacles stay movable there instead of silently refusing to drag. The
     * fallback projects the cursor onto the object's own height, which is the plane that
     * matters for placing things on the ground.
     */
    private get dragsDirectly() {
        return this.respondToMove || !activeRenderer.current?.attachGizmo;
    }

    constructor(x: number, y: number, z: number, palette: SelectionPalette, fieldIndex?: number) {
        this.px = signal(x);
        this.py = signal(y);
        this.pz = signal(z);
        this.state = signal("none");
        this.palette = palette;
        this.fieldIndex = signal(fieldIndex ?? 0);
    }

    hover() {
        this.state.value = "hover";
        appstate.needsRender.value = true;
    }

    unhover() {
        this.state.value = "none";

        if (this.movable)
            activeRenderer.current?.detachGizmo?.(this);

        appstate.needsRender.value = true;
    }

    select() {
        this.state.value = "select";
        appstate.needsRender.value = true;
    }

    selecthover() {
        this.state.value = "selecthover";

        if (this.movable)
            activeRenderer.current?.attachGizmo?.(this);

        appstate.needsRender.value = true;
    }

    /**
     * Called by the renderer's gizmo when it has moved this object.
     *
     * Takes the new world position rather than reading it back off a mesh, so the gizmo
     * implementation stays entirely inside whichever renderer provides one. Subclasses
     * override to undo whatever offset they apply when drawing.
     */
    transformMove(x: number, y: number, z: number) {
        if (this.movable)
            batch(() => {
                this.px.value = x;
                this.py.value = y;
                this.pz.value = z;
                appstate.needsRender.value = true;
            });
    }

    grab(ray: Ray) {
        if (this.movable && this.dragsDirectly)
        {
            const start = intersectPlaneY(ray, this.py.value);
            if (!start) return;

            this.state.value = "grab";
            this.grabOffset = {
                x: this.px.value - start.x,
                y: this.py.value - start.y,
                z: this.pz.value - start.z,
            };
            appstate.needsRender.value = true;
        }
    }

    move(ray: Ray) {
        if (this.movable && this.dragsDirectly && this.grabOffset)
        {
            const p = intersectPlaneY(ray, this.py.value);
            if (!p) return;

            batch(() => {
                this.px.value = this.grabOffset!.x + p.x;
                this.py.value = this.grabOffset!.y + p.y;
                this.pz.value = this.grabOffset!.z + p.z;
                appstate.needsRender.value = true;
            });
        }
    }

    ungrab(s: SelectionState) {
        if (this.dragsDirectly)
        {
            this.state.value = s;
            this.grabOffset = undefined;
            appstate.needsRender.value = true;
        }
    }

    isSelected() {
        const val = this.state.value;
        return val == "select" || val == "selecthover";
    }
}
