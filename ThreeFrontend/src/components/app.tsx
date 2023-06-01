import { Fragment, createContext, h } from 'preact';
import { Viewport } from './viewport/Viewport';
import { Start } from './hud/Start'
import { HoursPerTick } from './hud/HoursPerTick';
import { TotalHours } from './hud/TotalHours';
import { FieldResolution } from './hud/FieldResolution';
import { FieldSizeX } from './hud/FieldSizeX';
import { FieldSizeZ } from './hud/FieldSizeZ';
import { FieldSizeD } from './hud/FieldSizeD';
import { InitNumber } from './hud/InitNumber';
import { PlantsTable } from './viewport/PlantsTable';
import { Randomize } from './hud/Randomize';
import { Seeds } from './hud/Seeds';

const App = () => {
	return(
	<Fragment>
		<nav id="hud">
			<HoursPerTick/>
			<TotalHours/>
			<FieldResolution/>
			<FieldSizeX/>
			<FieldSizeZ/>
			<FieldSizeD/>
			<Randomize/>
			<InitNumber/>
			<hr/>
			<Seeds/>
			<hr/>
			<Start/>
		</nav>
		<main id="app">
			<PlantsTable/>
			{/*<Viewport />*/}
		</main>
	</Fragment>
)};

export default App;
