import { Fragment, createContext, h } from 'preact';
import appstate from '../appstate';
import { Start } from './hud/Start'
import { HoursPerTick } from './hud/HoursPerTick';
import { InitialDate } from './hud/InitialDate';
import { TotalHours } from './hud/TotalHours';
import { FieldResolution } from './hud/FieldResolution';
import { FieldSizeX } from './hud/FieldCellsX';
import { FieldSizeZ } from './hud/FieldCellsZ';
import { FieldSizeD } from './hud/FieldCellsD';
import { InitNumber } from './hud/InitNumber';
import { PickedDetails } from './viewport/PickedDetails';
import { Randomize } from './hud/Randomize';
import { Seeds } from './hud/Seeds';
import RendererHost from '../render/RendererHost';
import { ProgressBar } from './hud/ProgressBar';
import { Obstacles } from './hud/Obstacles';
import { Renderer } from './hud/RenderMode';
import { ExportImport } from './hud/ExportImport';
import { VisualMapping } from './hud/VisualMapping';
import { SpeciesList } from './hud/Species';
import { ExactPreview } from './hud/ExactPreview';
import { DownloadRoots } from './hud/DownloadRoots';
import { OfflineRender } from './hud/OfflineRender';
import { ViewportBackend } from './hud/ViewportBackend';
import { Lighting } from './hud/Lighting';
import { VoxelDebugControl } from './hud/VoxelDebugControl';
import { SamplesPerPixel } from './hud/SamplesPerPixel';
import { FieldModelUpload } from './hud/FieldModelUpload';
import { FieldItemRegex } from './hud/FieldItemRegex';
import { TerrainsList } from './viewport/TerrainsList';
import { SeedsDistributionUpload } from './hud/SeedsDistributionUpload';
//import {Tab, initTE } from "tw-elements"; initTE({ Tab }); //tried but failed

const App = () => {
	return(
	<>
		<main id="app">
			<RendererHost />
		</main>
		<nav id="hud">
			<ul role="tablist">
				<li role="presentation"><a role="tab" onClick={e => appstate.activeTab.value = "tab-home"} aria-selected={appstate.activeTab.value.endsWith("tab-home")}>Home</a></li>
				<li role="presentation"><a role="tab" onClick={e => appstate.activeTab.value = "tab-sim"} aria-selected={appstate.activeTab.value.endsWith("tab-sim")}>Simulation</a></li>
				<li role="presentation"><a role="tab" onClick={e => appstate.activeTab.value = "tab-terrain"} aria-selected={appstate.activeTab.value.endsWith("tab-terrain")}>Terrain</a></li>
				<li role="presentation"><a role="tab" onClick={e => appstate.activeTab.value = "tab-spec"} aria-selected={appstate.activeTab.value.endsWith("tab-spec")}>Species</a></li>
				<li role="presentation"><a role="tab" onClick={e => appstate.activeTab.value = "tab-plants"} aria-selected={appstate.activeTab.value.endsWith("tab-plants")}>Plants</a></li>
				<li role="presentation"><a role="tab" onClick={e => appstate.activeTab.value = "tab-obstacles"} aria-selected={appstate.activeTab.value.endsWith("tab-obstacles")}>Obstacles</a></li>
				<li role="presentation"><a role="tab" onClick={e => appstate.activeTab.value = "tab-analysis"} aria-selected={appstate.activeTab.value.endsWith("tab-analysis")}>Analysis</a></li>
				<li role="presentation"><a role="tab" onClick={e => appstate.activeTab.value = "tab-export"} aria-selected={appstate.activeTab.value.endsWith("tab-export")}>Export</a></li>
				{() => "TODO: make analysis a separate panel always visible"}
			</ul>

			<div role="tabpanel" id="tab-home" aria-selected={appstate.activeTab.value.endsWith("tab-home")}>
				<Start  inclStats={true}/>&nbsp;<ProgressBar/>
				<ExportImport/>
			</div>
			<div role="tabpanel" id="tab-sim" aria-selected={appstate.activeTab.value.endsWith("tab-spec")}>
				<SpeciesList/>
			</div>
			<div role="tabpanel" id="tab-sim" aria-selected={appstate.activeTab.value.endsWith("tab-sim")}>
				<HoursPerTick/>
				<InitialDate/>
				<TotalHours/>
				<Randomize/>
				<InitNumber/>
				<Renderer/>
				<SamplesPerPixel/>
				<ExactPreview/>
				<DownloadRoots/>
			</div>
			<div role="tabpanel" id="tab-plants" aria-selected={appstate.activeTab.value.endsWith("tab-plants")}>
				<SeedsDistributionUpload/>
				<Seeds/>
			</div>
			<div role="tabpanel" id="tab-terrain" aria-selected={appstate.activeTab.value.endsWith("tab-terrain")}>
				<FieldResolution/>
				<FieldSizeX/>
				<FieldSizeZ/>
				<FieldSizeD/>
				<FieldModelUpload/>
				<FieldItemRegex/>
				<TerrainsList/>
			</div>
			<div role="tabpanel" id="tab-obstacles" aria-selected={appstate.activeTab.value.endsWith("tab-obstacles")}>
				<Obstacles/>
			</div>
			<div role="tabpanel" id="tab-analysis" aria-selected={appstate.activeTab.value.endsWith("tab-analysis")}>
				<Start inclStats={false}/>
				<VisualMapping/>
				<Lighting/>
				<VoxelDebugControl/>
				<ViewportBackend/>
				<PickedDetails/>
			</div>

			<div role="tabpanel" id="tab-export" aria-selected={appstate.activeTab.value.endsWith("tab-export")}>
				<OfflineRender/>
			</div>
		</nav>
	</>
)};

export default App;
