import { Fragment, createContext, h } from 'preact';
import { Start } from './hud/Start'
import { HoursPerTick } from './hud/HoursPerTick';
import { TotalHours } from './hud/TotalHours';
import { FieldResolution } from './hud/FieldResolution';
import { FieldSizeX } from './hud/FieldCellsX';
import { FieldSizeZ } from './hud/FieldCellsZ';
import { FieldSizeD } from './hud/FieldCellsD';
import { InitNumber } from './hud/InitNumber';
import { PlantsTable } from './viewport/PlantsTable';
import { Randomize } from './hud/Randomize';
import { Seeds } from './hud/Seeds';
import ThreeSceneFn from './viewport/ThreeSceneFn';
import { ProgressBar } from './hud/ProgressBar';
import { Obstacles } from './hud/Obstacles';
import { Renderer } from './hud/RenderMode';
import { ExportImport } from './hud/ExportImport';
import { VisualMapping } from './hud/VisualMapping';
import { signal } from '@preact/signals';
import { SpeciesList } from './hud/Species';
import { ExactPreview } from './hud/ExactPreview';
import { DownloadRoots } from './hud/DownloadRoots';
import { SamplesPerPixel } from './hud/SamplesPerPixel';
import { FieldModelUpload } from './hud/FieldModelUpload';
import { FieldItemRegex } from './hud/FieldItemRegex';
//import {Tab, initTE } from "tw-elements"; initTE({ Tab }); //tried but failed

const tabs = signal("tab-home");

const App = () => {
	return(
	<>
		<main id="app">
			<ThreeSceneFn />
		</main>
		<nav id="hud">
			<ul role="tablist">
				<li role="presentation"><a role="tab" onClick={e => tabs.value = "tab-home"} aria-selected={tabs.value.endsWith("tab-home")}>Home</a></li>
				<li role="presentation"><a role="tab" onClick={e => tabs.value = "tab-sim"} aria-selected={tabs.value.endsWith("tab-sim")}>Simulation</a></li>
				<li role="presentation"><a role="tab" onClick={e => tabs.value = "tab-spec"} aria-selected={tabs.value.endsWith("tab-spec")}>Species</a></li>
				<li role="presentation"><a role="tab" onClick={e => tabs.value = "tab-plants"} aria-selected={tabs.value.endsWith("tab-plants")}>Plants</a></li>
				<li role="presentation"><a role="tab" onClick={e => tabs.value = "tab-obstacles"} aria-selected={tabs.value.endsWith("tab-obstacles")}>Obstacles</a></li>
				<li role="presentation"><a role="tab" onClick={e => tabs.value = "tab-analysis"} aria-selected={tabs.value.endsWith("tab-analysis")}>Analysis</a></li>
				{() => "TODO: make analysis a separate panel always visible"}
			</ul>

			<div role="tabpanel" id="tab-home" aria-selected={tabs.value.endsWith("tab-home")}>
				<Start  inclStats={true}/>&nbsp;<ProgressBar/>
				<ExportImport/>
			</div>
			<div role="tabpanel" id="tab-sim" aria-selected={tabs.value.endsWith("tab-spec")}>
				<SpeciesList/>
			</div>
			<div role="tabpanel" id="tab-sim" aria-selected={tabs.value.endsWith("tab-sim")}>
				<HoursPerTick/>
				<TotalHours/>
				<FieldResolution/>
				<FieldSizeX/>
				<FieldSizeZ/>
				<FieldSizeD/>
				<FieldModelUpload/>
				<FieldItemRegex/>
				<Randomize/>
				<InitNumber/>
				<Renderer/>
				<SamplesPerPixel/>
				<ExactPreview/>
				<DownloadRoots/>
			</div>
			<div role="tabpanel" id="tab-plants" aria-selected={tabs.value.endsWith("tab-plants")}>
				<Seeds/>
			</div>
			<div role="tabpanel" id="tab-obstacles" aria-selected={tabs.value.endsWith("tab-obstacles")}>
				<Obstacles/>
			</div>
			<div role="tabpanel" id="tab-analysis" aria-selected={tabs.value.endsWith("tab-analysis")}>
				<Start inclStats={false}/>
				<VisualMapping/>
				<PlantsTable/>
			</div>
		</nav>
	</>
)};

export default App;
