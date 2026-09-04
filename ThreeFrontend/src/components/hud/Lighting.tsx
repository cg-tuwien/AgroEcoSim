import { h } from "preact";
import appstate from "../../appstate";
import { VisualMappingOptions } from "../../render/core/PlantColors";

/**
 * Viewport lighting switches.
 *
 * Independent rather than a ladder: the ambient hemisphere is always on, and each of these
 * adds a term on top of it. Occlusion and indirect light attenuate the AMBIENT, so both are
 * meaningful with no sun at all - requiring shadows first would describe the order they were
 * built in, not the lighting.
 */
export function Lighting() {
    const tooltip = "Viewport lighting only. It does NOT affect the simulation, and it is "
        + "unrelated to Render mode (Ambient / Mitsuba3 / Tamashii), which selects the "
        + "light-transport engine the server uses to compute plant irradiance.";

    //Data ramps paint quantitative values into emissive; lighting them would make the same
    //number read as a different colour depending on which way a leaf faces. The renderer
    //enforces this too - the control just explains why it is greyed out.
    const dataMapping = appstate.visualMapping.value !== VisualMappingOptions.Natural;

    const indirect = appstate.lightIndirect.value;

    const box = (name: string, signal: { value: boolean }, label: string, hint: string, disabled = false) =>
        <span class="lighting-option">
            <input type="checkbox" name={name} id={name} title={hint} checked={signal.value}
                disabled={dataMapping || disabled}
                onChange={e => signal.value = e.currentTarget.checked} />
            <label for={name} title={hint}>{label}</label>
        </span>;

    return <div>
        <label title={tooltip}>Viewport lighting</label>
        {box('lightDaylight', appstate.lightDaylight,
            'daylight',
            'Physical daylight: a modest sky plus a dominant sun, instead of the flat ambient the viewport has always used. Shadowed surfaces become genuinely dark, which is what makes occlusion and indirect light visible at all. Off, the ambient alone is bright enough to hide both.')}
        {box('lightShadows', appstate.lightShadows,
            'shadows',
            'A directional sun. On WebGPU the shadow is traced volumetrically through the leaf-area-density field rather than from a shadow map, so a leaf can never shadow itself and the softness is the canopy\u2019s. The sun follows the start date and the current timestep, so a shadow shows where the simulation thinks the sun is.')}
        {box('lightOcclusion', appstate.lightOcclusion,
            'occlusion',
            'Screen-space ambient occlusion: darkens crevices and leaf clusters. Needs the WebGPU backend.')}
        {box('lightIndirect', appstate.lightIndirect,
            'indirect',
            'Cone-traced indirect light through a leaf-area-density field: canopy self-shading and colour bleed between leaves. Needs the WebGPU backend.')}
        {box('lightSecondBounce', appstate.lightSecondBounce,
            '2 bounces',
            'A second indirect bounce, which lifts the interior of a dense canopy. Only applies with indirect light on.',
            !indirect)}
        {/* No bias control any more. Both backends now trace the sun through a density field
            rather than comparing depths against a shadow map, so there is no comparison left to
            bias - and on WebGL the control never worked anyway: a zero-thickness leaf records
            its own depth, and the offset needed to clear that tie grows without bound as the
            leaf turns edge-on to the sun. */}
        {dataMapping
            ? <label class="unit">(ambient only while a data mapping is shown)</label>
            : null}
    </div>;
}
